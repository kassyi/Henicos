using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <remarks>
/// <para>
/// Memory (C9, decision §5-4, LLVM's <c>noclobber</c>): an element of memory that the threads share, read with a uniform
/// index, is uniform only when no path from the last barrier to the read writes the same memory. The memory is named by
/// the variable it is reached through: a parameter of array or span type, a <c>SharedMemory&lt;T&gt;</c>, or a field of
/// such a type (the arrays of a struct parameter, by field). Each one is a value of the forward data flow,
/// whose variance says how far the memory may differ since it was last written: a write makes it
/// <see cref="ThreadVariance.Block"/>, a block barrier lowers that to <see cref="ThreadVariance.Grid"/> (the other
/// blocks may still be writing), a grid barrier to <see cref="ThreadVariance.Uniform"/>. Shared memory belongs to one
/// block, so its elements are never more than block-uniform.
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
internal sealed partial class UniformityAnalysis
{
    private readonly Dictionary<ISymbol, int> _memory = new(SymbolEqualityComparer.Default);

    // The value indices of the memories named by a field, which a struct argument of a writing call may reach
    private readonly HashSet<int> _fieldMemory = [];

    // The value index of "a barrier on every path from the start" (DefineBarrierOnEveryPath)
    private int _barrierOnEveryPath = -1;

    private int ValueCount => _symbols.Count + _captures.Count + _memory.Count;

    // <target> written: an element of memory the threads share
    private void AddMemoryWrite(IOperation target)
    {
        if (target is IDeclarationExpressionOperation declaration)
        {
            target = declaration.Expression;
        }

        if (target is ITupleOperation tuple)
        {
            foreach (var element in tuple.Elements)
            {
                AddMemoryWrite(element);
            }

            return;
        }

        if (GetElementMemory(target) is { } memory)
        {
            AddMemoryEvent(FlowEventKind.MemoryWrite, MemoryOf(memory));
        }
    }

    // What a call writes besides its by-reference element arguments (written by AddMemoryWrite), after the barrier that a
    // device function passes on every path
    private void AddCallMemoryWrites(IInvocationOperation invocation)
    {
        var summary = TryGetDeviceFunction(invocation.TargetMethod, out var callee, out var declaration)
            ? GetCalleeSummary(callee, declaration, default)
            : default;
        if (!summary.MemoryAtExit.IsDefault && summary.BarrierOnEveryPath != ThreadVariance.Block)
        {
            AddMemorySync(summary.BarrierOnEveryPath == ThreadVariance.Uniform ? SyncScope.Grid : SyncScope.Block);
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                AddMemoryWrite(argument.Value);
            }
        }

        if (DeviceFunctionEffects.Classify(invocation.TargetMethod, _model.Compilation) != DeviceFunctionEffect.Memory)
        {
            return;
        }

        // A device function with a body: what its memory is where it returns; anything else writes it
        if (invocation.Instance is { } instance && GetMemory(instance) is { } receiver)
        {
            AddMemoryEvent(FlowEventKind.MemoryWrite, MemoryOf(receiver));
        }

        foreach (var argument in invocation.Arguments)
        {
            var left = summary.MemoryAtExit.IsDefault || argument.Parameter is not { } parameter
                ? ThreadVariance.Block
                : summary.MemoryAtExit[Math.Min(parameter.Ordinal, summary.MemoryAtExit.Length - 1)];
            if (GetMemory(argument.Value) is { } memory)
            {
                AddMemoryEvent(FlowEventKind.MemoryWrite, MemoryOf(memory), left);
            }
            else if (argument.Value.Type is INamedTypeSymbol { IsValueType: true, IsRefLikeType: false } structType)
            {
                // A struct argument carries the arrays of its fields
                var fieldsLeft = summary.MemoryAtExit.IsDefault ? ThreadVariance.Block : summary.FieldMemoryAtExit;
                AddFieldMemoryWrites(structType, fieldsLeft, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
            }
        }
    }

    // Each field of a struct (and of the structs it holds) that reaches memory the threads share
    private void AddFieldMemoryWrites(INamedTypeSymbol type, ThreadVariance written, HashSet<ITypeSymbol> visited)
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
                AddMemoryEvent(FlowEventKind.MemoryWrite, MemoryOf(field), written);
            }
            else if (field.Type is INamedTypeSymbol { IsValueType: true, IsRefLikeType: false } nested)
            {
                AddFieldMemoryWrites(nested, written, visited);
            }
        }
    }

    // How far the memory of each parameter, and of the fields, may differ where the analyzed method returns
    private (ImmutableArray<ThreadVariance> Parameters, ThreadVariance Fields) SummarizeMemory()
    {
        var exit = _in[_exit];
        var parameters = ImmutableArray.CreateBuilder<ThreadVariance>(_method.Parameters.Length);
        foreach (var parameter in _method.Parameters)
        {
            parameters.Add(_memory.TryGetValue(parameter, out var index) ? exit[index] : ThreadVariance.Uniform);
        }

        var fields = ThreadVariance.Uniform;
        foreach (var field in _fieldMemory)
        {
            fields |= exit[field];
        }

        return (parameters.MoveToImmutable(), fields);
    }

    // A block or grid barrier: what was written before it is the same for every thread that waited at it
    private void AddMemorySync(SyncScope scope) =>
        _current.Events.Add(new FlowEvent(FlowEventKind.MemorySync, null, null, null,
            variance: scope == SyncScope.Grid ? ThreadVariance.Uniform : ThreadVariance.Grid));

    // A write leaves the memory at most as variant as written: Block for a plain write
    private void AddMemoryEvent(FlowEventKind kind, int memory, ThreadVariance written = ThreadVariance.Block)
    {
        if (written != ThreadVariance.Uniform)
        {
            _current.Events.Add(new FlowEvent(kind, null, null, null, memory, written));
        }
    }

    // Whether a barrier lies on every path from the start of the analyzed method: a value that starts Block and that
    // MemorySync lowers like memory; as values merge by union, it stays below Block only where every path waited
    private void DefineBarrierOnEveryPath()
    {
        _barrierOnEveryPath = MemoryOf(_method);
        _current.Events.Add(new FlowEvent(FlowEventKind.Define, null, null, null, _barrierOnEveryPath,
            ThreadVariance.Block));
    }

    private int MemoryOf(ISymbol symbol)
    {
        if (!_memory.TryGetValue(symbol, out var index))
        {
            index = ValueCount;
            _memory.Add(symbol, index);
            if (symbol is IFieldSymbol)
            {
                _fieldMemory.Add(index);
            }
        }

        return index;
    }

    private void ApplyMemoryEvent(FlowEvent flowEvent, ThreadVariance[] state)
    {
        switch (flowEvent.Kind)
        {
            case FlowEventKind.MemoryWrite:
                state[flowEvent.Value] |= flowEvent.Variance;
                break;
            case FlowEventKind.MemorySync:
                // A block barrier leaves at most the difference between blocks; a grid barrier none
                foreach (var memory in _memory.Values)
                {
                    if (state[memory] != ThreadVariance.Uniform)
                    {
                        state[memory] = flowEvent.Variance;
                    }
                }

                break;
        }
    }

    // An element read: the variance of the memory since it was last written, at least block-uniform for shared memory
    private ThreadVariance ReadMemory(IOperation element, ThreadVariance[] state)
    {
        if (GetElementMemory(element) is not { } memory)
        {
            return ThreadVariance.Uniform;
        }

        var written = _memory.TryGetValue(memory, out var index) ? state[index] : ThreadVariance.Uniform;
        return GetMemoryType(memory).IsSharedMemoryType() ? written | ThreadVariance.Grid : written;
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
