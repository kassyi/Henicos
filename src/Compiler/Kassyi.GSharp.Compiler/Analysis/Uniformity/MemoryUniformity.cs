using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Follows, for a <see cref="UniformityAnalysis"/>, how far the memory that the threads share may differ since it was
/// last written, so that an element read with a uniform index is as variant as its memory.
/// </summary>
/// <remarks>
/// The design (the values, what writes and what lowers them, the call summaries) is in
/// <c>docs/spec/en/02_architecture/05_uniformity_analysis.md</c>; what it does not follow is listed on GSH0083.md.
/// </remarks>
/// <param name="recorder">The recorder whose nodes get the events and whose values the memory follows.</param>
internal sealed class MemoryUniformity(UniformityRecorder recorder)
{
    // The memories, each a value of the analysis: a parameter, field or property of array, span or SharedMemory<T>
    // type, a SharedMemory<T> local that is not a copy of another, and a ref or out parameter; and the method itself
    // for "a barrier on every path from the start" (OnStart)
    private readonly Dictionary<ISymbol, int> _memory = new(SymbolEqualityComparer.Default);

    // The value indices of the memories named by a field or property, which a struct argument of a call may reach
    private readonly HashSet<int> _fieldMemory = [];

    // The value indices of SharedMemory<T> memories, which are never the same across blocks
    private readonly HashSet<int> _sharedMemory = [];

    // What a local refers to: for a local of array, span or SharedMemory<T> type, the references assigned to it; for a
    // ref local, the locations it is bound to
    private readonly Dictionary<ILocalSymbol, ImmutableArray<IOperation>.Builder> _sources =
        new(SymbolEqualityComparer.Default);

    // The ref locals bound to a location this class does not follow (a pointer): they may reach any memory
    private readonly HashSet<ILocalSymbol> _unknownTargets = new(SymbolEqualityComparer.Default);

    private readonly Dictionary<IInvocationOperation, ImmutableArray<MemoryCallTarget>> _callTargets = new();

    // The summary of each call's callee and whether the call may write memory, read on first use
    private readonly Dictionary<IInvocationOperation, (CalleeVariance Summary, bool WritesMemory)> _callEffects = new();

    // The memories each location reaches, resolved on first use (after recording, when every source is known)
    private readonly Dictionary<IOperation, ImmutableArray<int>> _resolved = new();

    private ImmutableArray<int> _allMemory;

    // The value index of "a barrier on every path from the start"
    private int _barrierOnEveryPath = -1;

    /// <summary>The number of values the memory takes.</summary>
    public int Count => _memory.Count;

    /// <summary>
    /// Node 0: defines the value that says whether a barrier lies on every path from the start of
    /// <paramref name="method"/>.
    /// </summary>
    // It starts Block and barriers lower it like memory; as values merge by union, it stays below Block only where every
    // path waited (a must value, read at the exit by Summarize)
    public void OnStart(IMethodSymbol method, int node)
    {
        _barrierOnEveryPath = MemoryOf(method);
        AddEvent(node, new UniformityEvent(UniformityEventKind.Define, null, null, null, _barrierOnEveryPath,
            ThreadVariance.Block));
    }

    /// <summary>An assignment: binds a ref local, copies a reference to memory into a local, or writes a location.</summary>
    public void AddAssignment(IAssignmentOperation assignment, int node)
    {
        switch (assignment)
        {
            case ISimpleAssignmentOperation { IsRef: true, Target: ILocalReferenceOperation { Local: var refLocal } }:
                Bind(refLocal, assignment.Value);
                return;
            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation { Local: { IsRef: false } local } }
                when IsMemoryType(local.Type):
                Hold(local, assignment.Value);
                return;
            default:
                AddWrite(assignment.Target, node);
                return;
        }
    }

    /// <summary>
    /// <paramref name="target"/> written. A write is an assignment, compound assignment, increment or deconstruction
    /// whose target is a location of memory: an element (<c>arr[i]</c>, <c>span[i]</c>, <c>shared[i]</c>), a ref local
    /// bound to one, a <c>ref</c> / <c>out</c> parameter, or the result of a device function that returns by reference.
    /// </summary>
    public void AddWrite(IOperation target, int node)
    {
        target = Unwrap(target);
        if (target is ITupleOperation tuple)
        {
            foreach (var element in tuple.Elements)
            {
                AddWrite(element, node);
            }

            return;
        }

        if (IsLocation(target))
        {
            AllocateLocation(target);
            AddEvent(node, new UniformityEvent(UniformityEventKind.MemoryWrite, target, null, null,
                Variance: ThreadVariance.Block));
        }
    }

    /// <summary>
    /// A call: the barrier that the device function passes on every path, then what it writes (its by-reference
    /// locations, and when it may write memory, the memory of its receiver, arguments and struct fields).
    /// </summary>
    // A struct argument or receiver carries the memory of its fields, nested structs included
    public void AddCall(IInvocationOperation invocation, int node)
    {
        if (GpuMacroClassifier.Classify(invocation.TargetMethod) is
            { Kind: GpuMacroKind.ForUnroll or GpuMacroKind.SyncGrid, HasBody: true })
        {
            return;
        }

        var targets = ImmutableArray.CreateBuilder<MemoryCallTarget>();
        foreach (var argument in invocation.Arguments)
        {
            var value = Unwrap(argument.Value);
            if (argument.Parameter is { RefKind: RefKind.Ref or RefKind.Out } parameter && IsLocation(value))
            {
                AllocateLocation(value);
                targets.Add(new MemoryCallTarget(MemoryCallTargetKind.ByReference, value, Ordinal: parameter.Ordinal));
            }
        }

        if (invocation.Instance is { } instance)
        {
            AddReferenceTarget(instance, MemoryCallTargetKind.Receiver, ordinal: -1, targets);
        }

        foreach (var argument in invocation.Arguments)
        {
            AddReferenceTarget(argument.Value, MemoryCallTargetKind.Argument, argument.Parameter?.Ordinal ?? -1,
                targets);
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
            case { Kind: UniformityEventKind.MemoryWrite, Operation: { } target }:
                Write(Resolve(target), memoryEvent.Variance, current);
                break;
            case { Kind: UniformityEventKind.MemorySync }:
                Sync(memoryEvent.Variance, current);
                break;
            case { Kind: UniformityEventKind.MemoryCall, Operation: IInvocationOperation invocation }:
                ApplyCall(invocation, current, reader, compilation);
                break;
        }
    }

    /// <summary>
    /// A read of a location (an element, a ref local, the result of a device function that returns by reference): how
    /// far the memory it reaches may differ since it was last written.
    /// </summary>
    // Shared memory belongs to one block, so its elements are never more than block-uniform
    public ThreadVariance Read(IOperation location, ThreadVariance[] current)
    {
        var variance = ThreadVariance.Uniform;
        foreach (var memory in Resolve(location))
        {
            variance |= _sharedMemory.Contains(memory) ? current[memory] | ThreadVariance.Grid : current[memory];
        }

        return variance;
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

    // What a call writes, after the barrier that a device function passes on every path. A device function with a
    // body leaves each memory as its own analysis finds it where it returns (written after its last barrier, or only
    // before one); any other call leaves what it may write written. A by-reference location is written whenever the
    // callee writes its parameter; the other memory only when the call may write memory (DeviceFunctionEffects)
    private void ApplyCall(IInvocationOperation invocation, ThreadVariance[] current, UniformityValueReader reader,
        Compilation compilation)
    {
        var (summary, writesMemory) = GetCallEffect(invocation, reader, compilation);
        var analyzed = !summary.MemoryAtExit.IsDefault;
        if (analyzed && summary.BarrierOnEveryPath != ThreadVariance.Block)
        {
            Sync(summary.BarrierOnEveryPath == ThreadVariance.Uniform ? ThreadVariance.Uniform : ThreadVariance.Grid,
                current);
        }

        foreach (var target in _callTargets[invocation])
        {
            switch (target)
            {
                case { Kind: MemoryCallTargetKind.ByReference, Operation: { } location }:
                    Write(Resolve(location), analyzed ? AtExit(summary, target.Ordinal) : ThreadVariance.Block,
                        current);
                    break;
                case { Kind: MemoryCallTargetKind.Receiver, Operation: { } receiver } when writesMemory:
                    Write(ResolveReference(receiver), ThreadVariance.Block, current);
                    break;
                case { Kind: MemoryCallTargetKind.Argument, Operation: { } argument } when writesMemory:
                    Write(ResolveReference(argument), analyzed ? AtExit(summary, target.Ordinal) : ThreadVariance.Block,
                        current);
                    break;
                case { Kind: MemoryCallTargetKind.Field } when writesMemory:
                    current[target.Memory] |= analyzed ? summary.FieldMemoryAtExit : ThreadVariance.Block;
                    break;
            }
        }
    }

    // The callee's memory of a parameter where it returns; a parameter it has no entry for counts as written
    private static ThreadVariance AtExit(CalleeVariance summary, int ordinal) =>
        ordinal >= 0 && ordinal < summary.MemoryAtExit.Length ? summary.MemoryAtExit[ordinal] : ThreadVariance.Block;

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

    private static void Write(ImmutableArray<int> memories, ThreadVariance written, ThreadVariance[] current)
    {
        foreach (var memory in memories)
        {
            current[memory] |= written;
        }
    }

    // A block barrier leaves at most the difference between blocks (the other blocks may still be writing); a grid
    // barrier none
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

    // ref int r = ref <location>: a location of memory is followed; a variable of the thread (a local, a by-value
    // parameter, a field of either) is not memory; anything else (a pointer) may be any memory
    private void Bind(ILocalSymbol refLocal, IOperation value)
    {
        value = Unwrap(value);
        if (IsLocation(value))
        {
            AllocateLocation(value);
            AddSource(refLocal, value);
        }
        else if (value is not (ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation))
        {
            _unknownTargets.Add(refLocal);
        }
    }

    // <local> = <value> for a local of array, span or SharedMemory<T> type: a copy of a reference to memory is the same
    // memory; a SharedMemory<T> made here is a memory of its own; any other array (a stack array) belongs to the thread
    private void Hold(ILocalSymbol local, IOperation value)
    {
        if (GetName(value) is { } name)
        {
            AllocateName(name);
            AddSource(local, value);
        }
        else if (local.Type.IsSharedMemoryType())
        {
            MemoryOf(local);
        }
    }

    private void AddSource(ILocalSymbol local, IOperation source)
    {
        if (!_sources.TryGetValue(local, out var sources))
        {
            sources = ImmutableArray.CreateBuilder<IOperation>();
            _sources.Add(local, sources);
        }

        sources.Add(source);
    }

    // A reference to memory passed to a call, or a struct whose fields reach memory
    private void AddReferenceTarget(IOperation reference, MemoryCallTargetKind kind, int ordinal,
        ImmutableArray<MemoryCallTarget>.Builder targets)
    {
        if (GetName(reference) is { } name)
        {
            AllocateName(name);
            targets.Add(new MemoryCallTarget(kind, Unwrap(reference), Ordinal: ordinal));
        }
        else if (reference.Type is INamedTypeSymbol { IsValueType: true, IsRefLikeType: false } structType)
        {
            AddFieldTargets(structType, targets, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
        }
    }

    // Each field of a struct (and of the structs it holds) that reaches memory; an auto-property is named by the
    // property, as its readers name it
    private void AddFieldTargets(INamedTypeSymbol type, ImmutableArray<MemoryCallTarget>.Builder targets,
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

            if (IsMemoryType(field.Type))
            {
                var name = field.AssociatedSymbol is IPropertySymbol property ? property : (ISymbol)field;
                targets.Add(new MemoryCallTarget(MemoryCallTargetKind.Field, null, MemoryOf(name)));
            }
            else if (field.Type is INamedTypeSymbol { IsValueType: true, IsRefLikeType: false } nested)
            {
                AddFieldTargets(nested, targets, visited);
            }
        }
    }

    private void AddEvent(int node, UniformityEvent memoryEvent) => recorder.Nodes[node].Events.Add(memoryEvent);

    // Gives the memories that a location names directly a value index, before the graph is solved; the locals it
    // reaches were given theirs where they were assigned
    private void AllocateLocation(IOperation location)
    {
        switch (location)
        {
            case IArrayElementReferenceOperation array:
                AllocateReference(array.ArrayReference);
                break;
            case IPropertyReferenceOperation { Property.IsIndexer: true, Instance: { } instance }:
                AllocateReference(instance);
                break;
            case IParameterReferenceOperation { Parameter: var parameter }:
                MemoryOf(parameter);
                break;
            case IInvocationOperation invocation:
                foreach (var argument in invocation.Arguments)
                {
                    var value = Unwrap(argument.Value);
                    if (IsLocation(value))
                    {
                        AllocateLocation(value);
                    }
                    else
                    {
                        AllocateReference(value);
                    }
                }

                break;
        }
    }

    private void AllocateReference(IOperation reference)
    {
        if (GetName(reference) is { } name)
        {
            AllocateName(name);
        }
    }

    // Locals are memories only where Hold made them one
    private void AllocateName(ISymbol name)
    {
        if (name is not ILocalSymbol)
        {
            MemoryOf(name);
        }
    }

    private int MemoryOf(ISymbol symbol)
    {
        if (!_memory.TryGetValue(symbol, out var index))
        {
            index = recorder.ValueCount;
            _memory.Add(symbol, index);
            if (symbol is IFieldSymbol or IPropertySymbol)
            {
                _fieldMemory.Add(index);
            }

            if (GetMemoryType(symbol).IsSharedMemoryType())
            {
                _sharedMemory.Add(index);
            }
        }

        return index;
    }

    // The memories a location reaches, through the locals that refer to memory; all of them for a ref local bound to a
    // location that is not followed
    private ImmutableArray<int> Resolve(IOperation location)
    {
        location = Unwrap(location);
        if (_resolved.TryGetValue(location, out var resolved))
        {
            return resolved;
        }

        var memories = new HashSet<int>();
        var all = false;
        CollectLocation(location, memories, new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default), ref all);
        resolved = all ? GetAllMemory() : ImmutableArray.CreateRange(memories);
        _resolved.Add(location, resolved);
        return resolved;
    }

    private ImmutableArray<int> ResolveReference(IOperation reference)
    {
        reference = Unwrap(reference);
        if (_resolved.TryGetValue(reference, out var resolved))
        {
            return resolved;
        }

        var memories = new HashSet<int>();
        var all = false;
        CollectReference(reference, memories, new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default), ref all);
        resolved = all ? GetAllMemory() : ImmutableArray.CreateRange(memories);
        _resolved.Add(reference, resolved);
        return resolved;
    }

    private void CollectLocation(IOperation location, HashSet<int> memories, HashSet<ILocalSymbol> visited, ref bool all)
    {
        switch (Unwrap(location))
        {
            case IArrayElementReferenceOperation array:
                CollectReference(array.ArrayReference, memories, visited, ref all);
                break;
            case IPropertyReferenceOperation { Property.IsIndexer: true, Instance: { } instance }:
                CollectReference(instance, memories, visited, ref all);
                break;
            case ILocalReferenceOperation { Local: { IsRef: true } refLocal }:
                all |= _unknownTargets.Contains(refLocal);
                CollectSources(refLocal, memories, visited, isLocation: true, ref all);
                break;
            case IParameterReferenceOperation { Parameter: var parameter }
                when _memory.TryGetValue(parameter, out var index):
                memories.Add(index);
                break;
            case IInvocationOperation invocation:
                foreach (var argument in invocation.Arguments)
                {
                    var value = Unwrap(argument.Value);
                    if (IsLocation(value))
                    {
                        CollectLocation(value, memories, visited, ref all);
                    }
                    else
                    {
                        CollectReference(value, memories, visited, ref all);
                    }
                }

                break;
        }
    }

    private void CollectReference(IOperation reference, HashSet<int> memories, HashSet<ILocalSymbol> visited,
        ref bool all)
    {
        if (GetName(reference) is not { } name)
        {
            return;
        }

        if (_memory.TryGetValue(name, out var index))
        {
            memories.Add(index);
        }

        if (name is ILocalSymbol local)
        {
            CollectSources(local, memories, visited, isLocation: false, ref all);
        }
    }

    private void CollectSources(ILocalSymbol local, HashSet<int> memories, HashSet<ILocalSymbol> visited,
        bool isLocation, ref bool all)
    {
        if (!visited.Add(local) || !_sources.TryGetValue(local, out var sources))
        {
            return;
        }

        foreach (var source in sources)
        {
            if (isLocation)
            {
                CollectLocation(source, memories, visited, ref all);
            }
            else
            {
                CollectReference(source, memories, visited, ref all);
            }
        }
    }

    // Every memory but the barrier value
    private ImmutableArray<int> GetAllMemory()
    {
        if (_allMemory.IsDefault)
        {
            var all = ImmutableArray.CreateBuilder<int>(_memory.Count);
            foreach (var index in _memory.Values)
            {
                if (index != _barrierOnEveryPath)
                {
                    all.Add(index);
                }
            }

            _allMemory = all.ToImmutable();
        }

        return _allMemory;
    }

    // Whether target is a location that may be memory: an element, a ref local, a ref or out parameter, the result of
    // a call that returns by reference (a ref conditional reaches here as a flow capture, which Bind takes as any memory)
    private static bool IsLocation(IOperation target) => target switch
    {
        IArrayElementReferenceOperation => true,
        IPropertyReferenceOperation { Property.IsIndexer: true, Instance: not null } => true,
        ILocalReferenceOperation { Local.IsRef: true } => true,
        IParameterReferenceOperation { Parameter: { RefKind: RefKind.Ref or RefKind.Out } parameter } =>
            !IsMemoryType(parameter.Type),
        IInvocationOperation { TargetMethod.ReturnsByRef: true } => true,
        _ => false
    };

    // The variable through which reference reaches memory the threads share, or null; a pointer is not followed
    private static ISymbol? GetName(IOperation reference)
    {
        ISymbol? symbol = Unwrap(reference) switch
        {
            IParameterReferenceOperation parameter => parameter.Parameter,
            ILocalReferenceOperation { Local: { IsRef: false } local } => local,
            IFieldReferenceOperation field => field.Field,
            IPropertyReferenceOperation { Property: { IsIndexer: false } property } => property,
            _ => null
        };

        return symbol != null && IsMemoryType(GetMemoryType(symbol)) ? symbol : null;
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                case IDeclarationExpressionOperation declaration:
                    operation = declaration.Expression;
                    continue;
                default:
                    return operation;
            }
        }
    }

    private static ITypeSymbol? GetMemoryType(ISymbol symbol) => symbol switch
    {
        IParameterSymbol parameter => parameter.Type,
        ILocalSymbol local => local.Type,
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        _ => null
    };

    private static bool IsMemoryType(ITypeSymbol? type) =>
        type is IArrayTypeSymbol ||
        type.IsSharedMemoryType() ||
        (type is INamedTypeSymbol { Name: TypeConstants.SpanType or TypeConstants.ReadOnlySpanType } span &&
         span.ContainingNamespace.IsSystemNamespace());
}
