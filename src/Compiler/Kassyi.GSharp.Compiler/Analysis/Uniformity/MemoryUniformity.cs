using Microsoft.CodeAnalysis.Operations;
using static Kassyi.GSharp.Compiler.Analysis.MemoryOperations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Follows, for a <see cref="UniformityAnalysis"/>, how far the memory that the threads share may differ since it was
/// last written, so that an element read with a uniform index is as variant as its memory.
/// </summary>
/// <remarks>
/// The design (the values, what writes and what lowers them, the call summaries) is in
/// <c>docs/spec/en/02_architecture/05_uniformity_analysis.md</c>; what it does not follow is listed on GSH0083.md.
/// The memories are in <see cref="MemoryValues"/>, what the locals refer to in <see cref="MemoryReferences"/>; the
/// walks that fill and read them are <see cref="MemoryRecording"/>, <see cref="MemoryResolver"/> and
/// <see cref="MemoryCallEffects"/>.
/// </remarks>
/// <param name="recorder">The recorder whose nodes get the events and whose values the memory follows.</param>
internal sealed class MemoryUniformity(UniformityRecorder recorder)
{
    private readonly MemoryValues _values = new(recorder);

    private readonly MemoryReferences _references = new();

    private readonly Dictionary<IInvocationOperation, ImmutableArray<MemoryCallTarget>> _callTargets = new();

    // What each call may write, read on first use
    private readonly Dictionary<IInvocationOperation, MemoryCallEffect> _callEffects = new();

    /// <summary>The number of values the memory takes.</summary>
    public int Count => _values.Count;

    /// <summary>
    /// Node 0: defines the value that says whether a barrier lies on every path from the start of
    /// <paramref name="method"/>.
    /// </summary>
    // It starts Block and barriers lower it like memory; as values merge by union, it stays below Block only where
    // every path waited (a must value, read at the exit by Summarize)
    public void OnStart(IMethodSymbol method, int node)
    {
        var barrierOnEveryPath = _values.DefineBarrierOnEveryPath(method);
        AddEvent(node, new UniformityEvent(UniformityEventKind.Define, null, null, null, barrierOnEveryPath,
            ThreadVariance.Block));
    }

    /// <summary>
    /// An assignment: binds a ref local, copies a reference to memory into a local, or writes a location.
    /// </summary>
    public void AddAssignment(IAssignmentOperation assignment, int node)
    {
        switch (assignment)
        {
            case ISimpleAssignmentOperation { IsRef: true, Target: ILocalReferenceOperation { Local: var refLocal } }:
                MemoryRecording.Bind(_values, _references, refLocal, assignment.Value);
                return;
            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation { Local: { IsRef: false } local } }
                when IsMemoryType(local.Type):
                MemoryRecording.Hold(_values, _references, local, assignment.Value);
                return;
            default:
                AddWrite(assignment.Target, node);
                return;
        }
    }

    /// <summary>
    /// <paramref name="target"/> written. A write is an assignment, compound assignment, increment or deconstruction
    /// whose target is a location of memory: an element (<c>arr[i]</c>, <c>span[i]</c>, <c>shared[i]</c>), a field or
    /// auto-property of one (<c>arr[i].X</c>), a property or indexer of one whose setter is not readonly
    /// (<c>arr[i][0]</c>), a ref local bound to one, a <c>ref</c> / <c>out</c> parameter, or the result of a device
    /// function that returns by reference.
    /// </summary>
    public void AddWrite(IOperation target, int node)
    {
        target = ContainingLocation(target);
        if (target is ITupleOperation tuple)
        {
            AddTupleWrites(tuple, node);
            return;
        }

        if (IsLocation(target))
        {
            MemoryRecording.AllocateLocation(_values, target);
            AddEvent(node, new UniformityEvent(UniformityEventKind.MemoryWrite, target, null, null,
                Variance: ThreadVariance.Block));
        }
    }

    // (a[i], b[j]) = ...: each element of the deconstruction
    private void AddTupleWrites(ITupleOperation tuple, int node)
    {
        foreach (var element in tuple.Elements)
        {
            AddWrite(element, node);
        }
    }

    /// <summary>
    /// A call: the barrier that the device function passes on every path, then what it writes (its by-reference
    /// locations, and when it may write memory, the memory of its receiver, arguments and struct fields).
    /// </summary>
    public void AddCall(IInvocationOperation invocation, int node)
    {
        if (GpuMacroClassifier.Classify(invocation.TargetMethod) is
            { Kind: GpuMacroKind.ForUnroll or GpuMacroKind.SyncGrid, HasBody: true })
        {
            return;
        }

        _callTargets[invocation] = MemoryRecording.CollectCallTargets(_values, invocation);
        AddEvent(node, new UniformityEvent(UniformityEventKind.MemoryCall, invocation, null, null));
    }

    /// <summary>
    /// A block or grid barrier: what was written before it is the same for every thread that waited at it.
    /// </summary>
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
                ApplyWrite(target, memoryEvent.Variance, current);
                break;
            case { Kind: UniformityEventKind.MemorySync }:
                _values.Sync(memoryEvent.Variance, current);
                break;
            case { Kind: UniformityEventKind.MemoryCall, Operation: IInvocationOperation invocation }:
                ApplyCall(invocation, current, reader, compilation);
                break;
        }
    }

    private void ApplyWrite(IOperation target, ThreadVariance variance, ThreadVariance[] current) =>
        MemoryCallEffects.Write(MemoryResolver.Resolve(_values, _references, target), variance, current);

    private void ApplyCall(IInvocationOperation invocation, ThreadVariance[] current, UniformityValueReader reader,
        Compilation compilation) =>
        MemoryCallEffects.Apply(_values, _references, _callTargets[invocation],
            GetCallEffect(invocation, reader, compilation), current);

    /// <summary>
    /// A read of a location (an element, a ref local, the result of a device function that returns by reference): how
    /// far the memory it reaches may differ since it was last written.
    /// </summary>
    // Shared memory belongs to one block, so its elements are never more than block-uniform
    public ThreadVariance Read(IOperation location, ThreadVariance[] current)
    {
        var variance = ThreadVariance.Uniform;
        foreach (var memory in MemoryResolver.Resolve(_values, _references, location))
        {
            variance |= _values.IsShared(memory) ? current[memory] | ThreadVariance.Grid : current[memory];
        }

        return variance;
    }

    /// <summary>
    /// How far the memory of each parameter of <paramref name="method"/>, and of the fields, may differ where it
    /// returns, and the barrier that every path to its return passes, from the state <paramref name="exit"/> at its
    /// exit.
    /// </summary>
    public (ImmutableArray<ThreadVariance> Parameters, ThreadVariance Fields, ThreadVariance BarrierOnEveryPath)
        Summarize(IMethodSymbol method, ThreadVariance[] exit) => _values.Summarize(method, exit);

    private MemoryCallEffect GetCallEffect(IInvocationOperation invocation,
        UniformityValueReader reader, Compilation compilation)
    {
        if (!_callEffects.TryGetValue(invocation, out var effect))
        {
            effect = MemoryCallEffects.GetEffect(invocation, reader, compilation);
            _callEffects.Add(invocation, effect);
        }

        return effect;
    }

    private void AddEvent(int node, UniformityEvent memoryEvent) => recorder.Nodes[node].Events.Add(memoryEvent);
}
