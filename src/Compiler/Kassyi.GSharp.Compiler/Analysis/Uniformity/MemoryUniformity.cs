using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Follows, for a <see cref="UniformityAnalysis"/>, how far the memory that the threads share may differ since it was
/// last written: an element of it read with a uniform index is as variant as the memory (C9).
/// </summary>
/// <remarks>
/// <para>
/// Memory (C9, decision §5-4, LLVM's <c>noclobber</c>): an element of memory that the threads share, read with a uniform
/// index, is uniform only when no path from the last barrier to the read writes the same memory. The memory is named by
/// the variable it is reached through: a parameter of array or span type, a <c>SharedMemory&lt;T&gt;</c>, or a field of
/// such a type (the arrays of a struct parameter, by field). Each one is a value of the forward data flow, after the
/// locals, parameters and flow captures of <see cref="UniformityRecorder"/>, whose variance says how far the memory may
/// differ since it was last written: a write makes it <see cref="ThreadVariance.Block"/>, a block barrier lowers that
/// to <see cref="ThreadVariance.Grid"/> (the other blocks may still be writing), a grid barrier to
/// <see cref="ThreadVariance.Uniform"/>. Shared memory belongs to one block, so its elements are never more than
/// block-uniform.
/// </para>
/// <para>
/// A write is an assignment, compound assignment, increment or <c>ref</c> / <c>out</c> argument whose target is an
/// element of the memory, and a call that may write memory (<see cref="DeviceFunctionEffects"/>) to the memory of its
/// arguments and receiver, and for an argument of struct type to the memory of each of its fields (nested structs
/// included). A device function with a body leaves the memory of each parameter as its own analysis finds it where it
/// returns (written after its last barrier, or only before one); any other call leaves it written. A device function
/// that passes a barrier on every path to its return lowers all memory of the caller as that barrier does, before its
/// writes; one that some path leaves without a barrier (an early <c>return</c>) does not.
/// </para>
/// <para>
/// [WHY] Two assumptions remain, documented with the diagnostics: two parameters that receive the same array are two
/// memories (aliases are not followed), and the memory is clean where a device function starts, as its parameters are
/// uniform. Memory reached through a local of a value type (an inline array, a stackalloc span) belongs to one thread
/// and stays a value of the analysis. A pointer is not followed: Roslyn has no public operation for <c>p[i]</c>.
/// </para>
/// </remarks>
/// <param name="recorder">The recorder whose nodes get the events and whose values the memory follows.</param>
internal sealed class MemoryUniformity(UniformityRecorder recorder)
{
    // What a write of a call reaches, besides a parameter ordinal (_callTargets)
    private const int ByReference = -1;
    private const int Receiver = -2;
    private const int Field = -3;
    private const int NoParameter = -4;

    private readonly Dictionary<ISymbol, int> _memory = new(SymbolEqualityComparer.Default);

    // The value indices of the memories named by a field, which a struct argument of a writing call may reach
    private readonly HashSet<int> _fieldMemory = [];

    // The memories each call may write, with what reaches them: a parameter ordinal, or ByReference, Receiver, Field,
    // NoParameter
    private readonly Dictionary<IInvocationOperation, ImmutableArray<(int Memory, int Ordinal)>> _callTargets = new();

    // The summary of each call's callee and whether the call may write memory, read on first use
    private readonly Dictionary<IInvocationOperation, (CalleeVariance Summary, bool WritesMemory)> _callEffects = new();

    // The value index of "a barrier on every path from the start" (OnStart)
    private int _barrierOnEveryPath = -1;

    /// <summary>The number of values the memory takes.</summary>
    public int Count => _memory.Count;

    /// <summary>
    /// Node 0: defines the value that says whether a barrier lies on every path from the start of
    /// <paramref name="method"/>; it starts <see cref="ThreadVariance.Block"/> and barriers lower it like memory, so as
    /// values merge by union it stays below <see cref="ThreadVariance.Block"/> only where every path waited.
    /// </summary>
    public void OnStart(IMethodSymbol method, int node)
    {
        _barrierOnEveryPath = MemoryOf(method);
        AddEvent(node, new UniformityEvent(UniformityEventKind.Define, null, null, null, _barrierOnEveryPath,
            ThreadVariance.Block));
    }

    /// <summary><paramref name="target"/> written: an element of memory the threads share.</summary>
    public void AddWrite(IOperation target, int node)
    {
        if (target is IDeclarationExpressionOperation declaration)
        {
            target = declaration.Expression;
        }

        if (target is ITupleOperation tuple)
        {
            foreach (var element in tuple.Elements)
            {
                AddWrite(element, node);
            }

            return;
        }

        if (GetElementMemory(target) is { } memory)
        {
            AddEvent(node, new UniformityEvent(UniformityEventKind.MemoryWrite, null, null, null, MemoryOf(memory),
                ThreadVariance.Block));
        }
    }

    /// <summary>
    /// A call: the barrier that the device function passes on every path, then what it writes (its by-reference element
    /// arguments, and when it may write memory, the memory of its receiver, arguments and struct argument fields).
    /// </summary>
    public void AddCall(IInvocationOperation invocation, int node)
    {
        if (GpuMacroClassifier.Classify(invocation.TargetMethod) is
            { Kind: GpuMacroKind.ForUnroll or GpuMacroKind.SyncGrid, HasBody: true })
        {
            return;
        }

        var targets = ImmutableArray.CreateBuilder<(int Memory, int Ordinal)>();
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                AddElementTargets(argument.Value, targets);
            }
        }

        if (invocation.Instance is { } instance && GetMemory(instance) is { } receiver)
        {
            targets.Add((MemoryOf(receiver), Receiver));
        }

        foreach (var argument in invocation.Arguments)
        {
            if (GetMemory(argument.Value) is { } memory)
            {
                targets.Add((MemoryOf(memory), argument.Parameter?.Ordinal ?? NoParameter));
            }
            else if (argument.Value.Type is INamedTypeSymbol { IsValueType: true, IsRefLikeType: false } structType)
            {
                // A struct argument carries the arrays of its fields
                AddFieldTargets(structType, targets, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
            }
        }

        _callTargets[invocation] = targets.ToImmutable();
        AddEvent(node, new UniformityEvent(UniformityEventKind.MemoryCall, invocation, null, null));
    }

    /// <summary>A block or grid barrier: what was written before it is the same for every thread that waited at it.</summary>
    public void AddSync(SyncScope scope, int node) =>
        AddEvent(node, new UniformityEvent(UniformityEventKind.MemorySync, null, null, null,
            Variance: scope == SyncScope.Grid ? ThreadVariance.Uniform : ThreadVariance.Grid));

    /// <summary>What a memory event does to <paramref name="current"/>.</summary>
    public void Apply(UniformityEvent memoryEvent, ThreadVariance[] current, UniformityValueReader reader,
        Compilation compilation)
    {
        switch (memoryEvent)
        {
            case { Kind: UniformityEventKind.MemoryWrite }:
                current[memoryEvent.Value] |= memoryEvent.Variance;
                break;
            case { Kind: UniformityEventKind.MemorySync }:
                Sync(memoryEvent.Variance, current);
                break;
            case { Kind: UniformityEventKind.MemoryCall, Operation: IInvocationOperation invocation }:
                ApplyCall(invocation, current, reader, compilation);
                break;
        }
    }

    /// <summary>An element read: the variance of the memory since it was last written, at least block-uniform for shared memory.</summary>
    public ThreadVariance Read(IOperation element, ThreadVariance[] current)
    {
        if (GetElementMemory(element) is not { } memory)
        {
            return ThreadVariance.Uniform;
        }

        var written = _memory.TryGetValue(memory, out var index) ? current[index] : ThreadVariance.Uniform;
        return GetMemoryType(memory).IsSharedMemoryType() ? written | ThreadVariance.Grid : written;
    }

    /// <summary>
    /// How far the memory of each parameter of <paramref name="method"/>, and of the fields, may differ where it returns,
    /// and the barrier that every path to its return passes, from the state <paramref name="exit"/> at its exit.
    /// </summary>
    public (ImmutableArray<ThreadVariance> Parameters, ThreadVariance Fields, ThreadVariance BarrierOnEveryPath)
        Summarize(IMethodSymbol method, ThreadVariance[] exit)
    {
        var parameters = ImmutableArray.CreateBuilder<ThreadVariance>(method.Parameters.Length);
        foreach (var parameter in method.Parameters)
        {
            parameters.Add(_memory.TryGetValue(parameter, out var index) ? exit[index] : ThreadVariance.Uniform);
        }

        var fields = ThreadVariance.Uniform;
        foreach (var field in _fieldMemory)
        {
            fields |= exit[field];
        }

        var barrier = _barrierOnEveryPath >= 0 ? exit[_barrierOnEveryPath] : ThreadVariance.Block;
        return (parameters.MoveToImmutable(), fields, barrier);
    }

    // What a call writes besides its by-reference element arguments, after the barrier that a device function passes on
    // every path; a device function with a body leaves its memory as it is where it returns, anything else writes it
    private void ApplyCall(IInvocationOperation invocation, ThreadVariance[] current, UniformityValueReader reader,
        Compilation compilation)
    {
        var (summary, writesMemory) = GetCallEffect(invocation, reader, compilation);
        if (!summary.MemoryAtExit.IsDefault && summary.BarrierOnEveryPath != ThreadVariance.Block)
        {
            Sync(summary.BarrierOnEveryPath == ThreadVariance.Uniform ? ThreadVariance.Uniform : ThreadVariance.Grid,
                current);
        }

        foreach (var (memory, ordinal) in _callTargets[invocation])
        {
            if (ordinal == ByReference)
            {
                current[memory] |= ThreadVariance.Block;
                continue;
            }

            if (!writesMemory)
            {
                continue;
            }

            current[memory] |= ordinal switch
            {
                _ when summary.MemoryAtExit.IsDefault || ordinal is Receiver or NoParameter => ThreadVariance.Block,
                Field => summary.FieldMemoryAtExit,
                _ => summary.MemoryAtExit[Math.Min(ordinal, summary.MemoryAtExit.Length - 1)]
            };
        }
    }

    private (CalleeVariance Summary, bool WritesMemory) GetCallEffect(IInvocationOperation invocation,
        UniformityValueReader reader, Compilation compilation)
    {
        if (_callEffects.TryGetValue(invocation, out var effect))
        {
            return effect;
        }

        var summary = reader.TryGetDeviceFunction(invocation.TargetMethod, out var callee, out var declaration)
            ? reader.GetCalleeSummary(callee, declaration, default)
            : default;
        effect = (summary, DeviceFunctionEffects.Classify(invocation.TargetMethod, compilation) == DeviceFunctionEffect.Memory);
        _callEffects.Add(invocation, effect);
        return effect;
    }

    // A block barrier leaves at most the difference between blocks; a grid barrier none
    private void Sync(ThreadVariance left, ThreadVariance[] current)
    {
        foreach (var memory in _memory.Values)
        {
            if (current[memory] != ThreadVariance.Uniform)
            {
                current[memory] = left;
            }
        }
    }

    // The element targets of a by-reference argument (a tuple of them included)
    private void AddElementTargets(IOperation target, ImmutableArray<(int Memory, int Ordinal)>.Builder targets)
    {
        if (target is IDeclarationExpressionOperation declaration)
        {
            target = declaration.Expression;
        }

        if (target is ITupleOperation tuple)
        {
            foreach (var element in tuple.Elements)
            {
                AddElementTargets(element, targets);
            }

            return;
        }

        if (GetElementMemory(target) is { } memory)
        {
            targets.Add((MemoryOf(memory), ByReference));
        }
    }

    // Each field of a struct (and of the structs it holds) that reaches memory the threads share
    private void AddFieldTargets(INamedTypeSymbol type, ImmutableArray<(int Memory, int Ordinal)>.Builder targets,
        HashSet<ITypeSymbol> visited)
    {
        if (!visited.Add(type))
        {
            return;
        }

        foreach (var member in type.GetMembers())
        {
            if (member is not IFieldSymbol { IsStatic: false } field)
            {
                continue;
            }

            if (IsSharedMemoryKind(field.Type))
            {
                targets.Add((MemoryOf(field), Field));
            }
            else if (field.Type is INamedTypeSymbol { IsValueType: true, IsRefLikeType: false } nested)
            {
                AddFieldTargets(nested, targets, visited);
            }
        }
    }

    private void AddEvent(int node, UniformityEvent memoryEvent) => recorder.Nodes[node].Events.Add(memoryEvent);

    private int MemoryOf(ISymbol symbol)
    {
        if (!_memory.TryGetValue(symbol, out var index))
        {
            index = recorder.ValueCount;
            _memory.Add(symbol, index);
            if (symbol is IFieldSymbol)
            {
                _fieldMemory.Add(index);
            }
        }

        return index;
    }

    // The memory that an element access reads or writes: arr[i], span[i], shared[i], ctx.Data[i]
    private static ISymbol? GetElementMemory(IOperation element) => element switch
    {
        IArrayElementReferenceOperation array => GetMemory(array.ArrayReference),
        IPropertyReferenceOperation { Property.IsIndexer: true, Instance: { } instance } => GetMemory(instance),
        _ => null
    };

    // The variable through which reference reaches memory the threads share, or null
    private static ISymbol? GetMemory(IOperation reference)
    {
        while (reference is IConversionOperation conversion)
        {
            reference = conversion.Operand;
        }

        ISymbol? symbol = reference switch
        {
            IParameterReferenceOperation parameter => parameter.Parameter,
            ILocalReferenceOperation { Local: var local } when local.Type.IsSharedMemoryType() => local,
            IFieldReferenceOperation field => field.Field,
            _ => null
        };

        return symbol != null && IsSharedMemoryKind(GetMemoryType(symbol)) ? symbol : null;
    }

    private static ITypeSymbol? GetMemoryType(ISymbol symbol) => symbol switch
    {
        IParameterSymbol parameter => parameter.Type,
        ILocalSymbol local => local.Type,
        IFieldSymbol field => field.Type,
        _ => null
    };

    private static bool IsSharedMemoryKind(ITypeSymbol? type) =>
        type is IArrayTypeSymbol ||
        type.IsSharedMemoryType() ||
        (type is INamedTypeSymbol { Name: TypeConstants.SpanType or TypeConstants.ReadOnlySpanType } span &&
         span.ContainingNamespace.IsSystemNamespace());
}
