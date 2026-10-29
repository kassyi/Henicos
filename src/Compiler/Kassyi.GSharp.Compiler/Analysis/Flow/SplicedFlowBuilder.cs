using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Builds the <see cref="SplicedFlowGraph"/> of a body once, for every analysis that needs it, and lets each record
/// what the operations do (<see cref="IFlowRecorder"/>): the liveness of the cross-barrier rules
/// (<see cref="CrossBarrierLiveness"/>) and the uniformity analysis (<see cref="UniformityAnalysis"/>).
/// </summary>
/// <remarks>
/// <para>
/// The basic blocks of the Roslyn graph become nodes, connected as the graph connects them; a branch with no
/// destination (a <c>throw</c>) leads to the exit block. The operations are walked in the order C# evaluates them: an
/// assignment evaluates its value, then its target; a call its receiver and arguments, then splits, then writes its
/// <c>ref</c> and <c>out</c> arguments.
/// </para>
/// <para>
/// Bodies are spliced where they run, by one rule for every analysis: the body of <c>Gpu.ForUnroll</c> (as a loop) and
/// of <c>Gpu.SyncGrid(body)</c> where the call expands it; a lambda where it is created; a local function where it is
/// called (once on each path: a recursive call adds nothing) and where it is converted to a delegate. A lambda or local
/// function body is opaque (<see cref="SplicedBody.IsOpaque"/>): it may run again later, and a recorder decides what
/// that means for it.
/// </para>
/// <para>
/// The state machine splits (<see cref="SplitPoint"/>) at a call that <see cref="SyncIntrinsicClassifier"/> marks
/// (barriers, warp shuffles) or that suspends (<see cref="SuspensionAnalysis"/>), at a <c>Gpu.SyncGrid(body)</c> with a
/// gap, and in each gap of its body (<see cref="SyncGridBody.IsSyncPoint"/>): between two operations of a block, or on
/// an edge between blocks, through a node of its own.
/// </para>
/// </remarks>
internal sealed class SplicedFlowBuilder
{
    private readonly SemanticModel _model;
    private readonly ImmutableArray<IFlowRecorder> _recorders;
    private readonly ImmutableArray<SplicedFlowNode>.Builder _nodes = ImmutableArray.CreateBuilder<SplicedFlowNode>();
    private readonly HashSet<IMethodSymbol> _localFunctionsInProgress = new(SymbolEqualityComparer.Default);

    // The state of the graph being built: the node that receives the next operations, the gaps of the Gpu.SyncGrid body
    // being built (null outside one), the element range of the last top-level operation and of the current one
    private SplicedFlowNode _current;
    private SyncGridGaps? _gaps;
    private SyncGridElementRange? _lastRange;
    private SyncGridElementRange? _topRange;

    private SplicedFlowBuilder(SemanticModel model, ImmutableArray<IFlowRecorder> recorders)
    {
        _model = model;
        _recorders = recorders;
        _current = NewNode();
    }

    /// <summary>Builds the graph of <paramref name="graph"/>, the body of a method, for <paramref name="recorders"/>.</summary>
    public static SplicedFlowGraph Build(ControlFlowGraph graph, SemanticModel model,
        ImmutableArray<IFlowRecorder> recorders)
    {
        var builder = new SplicedFlowBuilder(model, recorders);
        var start = builder._current;
        var (entry, exit) = builder.Build(graph, gaps: null, isRoot: true);
        Connect(start, entry);
        return new SplicedFlowGraph(builder._nodes.ToImmutable(), exit.Index);
    }

    // Builds the nodes of graph; returns the node of its entry block and the last node of its exit block
    private (SplicedFlowNode Entry, SplicedFlowNode Exit) Build(ControlFlowGraph graph, SyncGridGaps? gaps, bool isRoot)
    {
        var (savedCurrent, savedGaps, savedLast, savedTop) = (_current, _gaps, _lastRange, _topRange);
        _gaps = gaps;

        var blocks = graph.Blocks;
        var starts = new SplicedFlowNode[blocks.Length];
        var ends = new SplicedFlowNode[blocks.Length];
        for (var i = 0; i < blocks.Length; i++)
        {
            starts[i] = _current = NewNode();
            _lastRange = null;
            BuildBlock(blocks[i], graph, isRoot);
            ends[i] = _current;
        }

        for (var i = 0; i < blocks.Length - 1; i++)
        {
            ConnectBlock(blocks, starts, ends, i);
        }

        var result = (starts[0], ends[blocks.Length - 1]);
        (_current, _gaps, _lastRange, _topRange) = (savedCurrent, savedGaps, savedLast, savedTop);
        return result;
    }

    // The operations of a block, then its branch value: the condition of a branch, or the value the method returns
    private void BuildBlock(BasicBlock block, ControlFlowGraph graph, bool isRoot)
    {
        foreach (var operation in block.Operations)
        {
            WalkTop(operation, graph);
        }

        if (block.BranchValue is not { } value)
        {
            return;
        }

        WalkTop(value, graph);
        if (block.ConditionalSuccessor != null && GetConstantCondition(block) == null)
        {
            _current.Condition = value;
            _current.Graph = graph;
        }
        else if (isRoot && block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return)
        {
            _current.ReturnValue = value;
            _current.Graph = graph;
        }
    }

    // The edges of a block; a branch with no destination (throw) leaves through the exit block. A branch whose condition
    // is always true or always false (the discard arm of a switch expression, whose other side throws
    // SwitchExpressionException) keeps only the side it takes
    private void ConnectBlock(ImmutableArray<BasicBlock> blocks, SplicedFlowNode[] starts, SplicedFlowNode[] ends, int source)
    {
        var block = blocks[source];
        var exitBlock = blocks.Length - 1;
        var jumpsWhenTrue = block.ConditionKind == ControlFlowConditionKind.WhenTrue;
        var jumps = block.ConditionalSuccessor != null && GetConstantCondition(block) is { } constant
            ? constant == jumpsWhenTrue
            : (bool?)null;
        var fall = block.FallThroughSuccessor is { } fallThrough && jumps != true
            ? ConnectEdge(blocks, starts, ends, source, fallThrough.Destination?.Ordinal ?? exitBlock)
            : -1;
        if (block.ConditionalSuccessor is not { } conditional || jumps == false)
        {
            return;
        }

        var jump = ConnectEdge(blocks, starts, ends, source, conditional.Destination?.Ordinal ?? exitBlock);
        if (jumps == true)
        {
            return;
        }

        ends[source].WhenTrue = jumpsWhenTrue ? jump : fall;
        ends[source].WhenFalse = jumpsWhenTrue ? fall : jump;
    }

    // The value of a branch condition known without running it: a discard pattern always matches, a constant is itself
    private static bool? GetConstantCondition(BasicBlock block) =>
        block.BranchValue switch
        {
            IIsPatternOperation { Pattern: IDiscardPatternOperation } => true,
            { ConstantValue: { HasValue: true, Value: bool value } } => value,
            _ => null
        };

    // An edge between two blocks; in a Gpu.SyncGrid body, through a split node when the edge crosses a gap. Returns the
    // node the edge leads to.
    private int ConnectEdge(ImmutableArray<BasicBlock> blocks, SplicedFlowNode[] starts, SplicedFlowNode[] ends,
        int source, int target)
    {
        if (_gaps is { } gaps &&
            RangeBefore(blocks, ends, source, blocks.Length) is { } before &&
            RangeAfter(blocks, starts, target, blocks.Length) is { } after &&
            gaps.HasGap(before.Last, after.First))
        {
            var split = NewNode();
            Record(static (recorder, node) => recorder.OnSplit(new SplitPoint(SplitKind.Gap, null, null, null, null), node),
                split.Index);
            Connect(ends[source], split);
            Connect(split, starts[target]);
            return split.Index;
        }

        Connect(ends[source], starts[target]);
        return starts[target].Index;
    }

    // The element range of the last operation run before leaving block, looking back through empty blocks
    private static SyncGridElementRange? RangeBefore(ImmutableArray<BasicBlock> blocks, SplicedFlowNode[] ends, int block,
        int depth)
    {
        if (ends[block].Last is { } last)
        {
            return last;
        }

        var predecessors = blocks[block].Predecessors;
        return depth > 0 && predecessors.Length == 1
            ? RangeBefore(blocks, ends, predecessors[0].Source.Ordinal, depth - 1)
            : null;
    }

    // The element range of the first operation run after entering block, looking ahead through empty blocks
    private static SyncGridElementRange? RangeAfter(ImmutableArray<BasicBlock> blocks, SplicedFlowNode[] starts,
        int block, int depth)
    {
        if (starts[block].First is { } first)
        {
            return first;
        }

        var basicBlock = blocks[block];
        return depth > 0 && basicBlock.ConditionalSuccessor == null &&
               basicBlock.FallThroughSuccessor?.Destination is { } next
            ? RangeAfter(blocks, starts, next.Ordinal, depth - 1)
            : null;
    }

    // An operation of a block: in a Gpu.SyncGrid body, a split comes first when a gap lies between it and the operation
    // before it in the same block
    private void WalkTop(IOperation operation, ControlFlowGraph graph)
    {
        _topRange = _gaps?.RangeOf(operation.Syntax);
        if (_topRange is { } range && _gaps is { } gaps)
        {
            if (_lastRange is { } last && gaps.HasGap(last.Last, range.First))
            {
                Record(static (recorder, node) => recorder.OnSplit(new SplitPoint(SplitKind.Gap, null, null, null, null), node),
                    _current.Index);
            }

            _current.First ??= range;
        }

        Walk(operation, graph);
        foreach (var recorder in _recorders)
        {
            recorder.OnTopOperation(operation, graph, _current.Index);
        }

        if (_topRange is { } walked)
        {
            _current.First ??= walked;
            _current.Last = walked;
            _lastRange = walked;
        }
    }

    // The operations of operation in the order C# evaluates them
    private void Walk(IOperation operation, ControlFlowGraph graph)
    {
        switch (operation)
        {
            case IInvocationOperation invocation: WalkInvocation(invocation, graph); return;
            case IFlowAnonymousFunctionOperation function: SpliceLambda(function, graph); return;
            case IDelegateCreationOperation { Target: IMethodReferenceOperation { Method.MethodKind: MethodKind.LocalFunction } reference }:
                SpliceLocalFunction(reference.Method.OriginalDefinition, graph, SplicedBodyKind.LocalFunctionDelegate); return;
            case ILocalReferenceOperation reference: WalkLocalReference(reference); return;
            case ISimpleAssignmentOperation assignment: WalkSimpleAssignment(assignment, graph); return;
            case IDeconstructionAssignmentOperation deconstruction: WalkDeconstruction(deconstruction, graph); return;
            case IAssignmentOperation assignment: WalkCompoundAssignment(assignment, graph); return;
            case IIncrementOrDecrementOperation increment: WalkIncrement(increment, graph); return;
            case IFlowCaptureOperation capture: WalkFlowCapture(capture, graph); return;
            case IObjectCreationOperation creation: WalkObjectCreation(creation, graph); return;
        }

        WalkChildren(operation, graph);
    }

    private void WalkChildren(IOperation operation, ControlFlowGraph graph)
    {
        foreach (var child in operation.ChildOperations)
        {
            Walk(child, graph);
        }
    }

    private void WalkLocalReference(ILocalReferenceOperation reference)
    {
        foreach (var recorder in _recorders)
        {
            recorder.OnLocalReference(reference, _current.Index);
        }
    }

    // <target> = <value>: the value first; a local target is left to the recorders
    private void WalkSimpleAssignment(ISimpleAssignmentOperation assignment, ControlFlowGraph graph)
    {
        foreach (var recorder in _recorders)
        {
            recorder.BeforeAssignmentValue(assignment, _current.Index);
        }

        Walk(assignment.Value, graph);
        if (assignment.Target is not ILocalReferenceOperation)
        {
            Walk(assignment.Target, graph);
        }

        RecordAssignment(assignment, graph);
    }

    // (a, span[i]) = <value>: the value, then the elements that are not plain locals
    private void WalkDeconstruction(IDeconstructionAssignmentOperation deconstruction, ControlFlowGraph graph)
    {
        Walk(deconstruction.Value, graph);
        WalkDeconstructionTarget(deconstruction.Target, graph);
        RecordAssignment(deconstruction, graph);
    }

    private void WalkDeconstructionTarget(IOperation target, ControlFlowGraph graph)
    {
        switch (target)
        {
            case ITupleOperation tuple:
                foreach (var element in tuple.Elements)
                {
                    WalkDeconstructionTarget(element, graph);
                }

                return;
            case ILocalReferenceOperation { Local.IsRef: false }:
                return;
            default:
                Walk(target, graph);
                return;
        }
    }

    // span[i] = <value>, *p = <value>, x += <value>: the value first, then the target
    private void WalkCompoundAssignment(IAssignmentOperation assignment, ControlFlowGraph graph)
    {
        Walk(assignment.Value, graph);
        Walk(assignment.Target, graph);
        RecordAssignment(assignment, graph);
    }

    private void RecordAssignment(IAssignmentOperation assignment, ControlFlowGraph graph)
    {
        foreach (var recorder in _recorders)
        {
            recorder.OnAssignment(assignment, graph, _current.Index);
        }
    }

    private void WalkIncrement(IIncrementOrDecrementOperation increment, ControlFlowGraph graph)
    {
        Walk(increment.Target, graph);
        foreach (var recorder in _recorders)
        {
            recorder.OnIncrement(increment, graph, _current.Index);
        }
    }

    private void WalkFlowCapture(IFlowCaptureOperation capture, ControlFlowGraph graph)
    {
        Walk(capture.Value, graph);
        foreach (var recorder in _recorders)
        {
            recorder.OnFlowCapture(capture, graph, _current.Index);
        }
    }

    private void WalkObjectCreation(IObjectCreationOperation creation, ControlFlowGraph graph)
    {
        foreach (var recorder in _recorders)
        {
            recorder.OnObjectCreation(creation, _current.Index);
        }

        WalkChildren(creation, graph);
    }

    // <receiver> <arguments> <local function body> <split> <ref and out arguments written>
    private void WalkInvocation(IInvocationOperation invocation, ControlFlowGraph graph)
    {
        var method = invocation.TargetMethod;
        var macro = GpuMacroClassifier.Classify(method);
        if (macro is { Kind: GpuMacroKind.ForUnroll or GpuMacroKind.SyncGrid, HasBody: true })
        {
            WalkMacro(invocation, macro, graph);
            return;
        }

        if (invocation.Instance is { } instance)
        {
            Walk(instance, graph);
        }

        foreach (var argument in invocation.Arguments)
        {
            // M(out local): the callee writes the local when it returns (OnCallReturned)
            if (argument is not { Parameter.RefKind: RefKind.Out, Value: ILocalReferenceOperation { Local.IsRef: false } })
            {
                Walk(argument.Value, graph);
            }
        }

        if (method.MethodKind == MethodKind.LocalFunction)
        {
            SpliceLocalFunction(method.OriginalDefinition, graph, SplicedBodyKind.LocalFunctionCall);
        }

        foreach (var recorder in _recorders)
        {
            recorder.OnCall(invocation, _current.Index);
        }

        if (GetSplit(invocation, graph) is { } split)
        {
            Record((recorder, node) => recorder.OnSplit(split, node), _current.Index);
        }

        foreach (var recorder in _recorders)
        {
            recorder.OnCallReturned(invocation, graph, _current.Index);
        }
    }

    // Where a call splits the state machine; a barrier, or a call that reaches a block barrier, is also a site of GSH0037
    private SplitPoint? GetSplit(IInvocationOperation invocation, ControlFlowGraph graph)
    {
        var method = invocation.TargetMethod;
        var intrinsic = SyncIntrinsicClassifier.Classify(method);
        if (intrinsic.Kind is SyncIntrinsicKind.BlockBarrier or SyncIntrinsicKind.GridBarrier)
        {
            return new SplitPoint(SplitKind.Call, invocation.Syntax, intrinsic.Scope, null, graph);
        }

        if (intrinsic.SplitsStateMachine)
        {
            return new SplitPoint(SplitKind.Call, invocation.Syntax, null, null, graph);
        }

        if (!SuspensionAnalysis.IsSuspendingFunction(method, _model.Compilation))
        {
            return null;
        }

        return SuspensionAnalysis.ReachesBlockBarrier(method, _model.Compilation)
            ? new SplitPoint(SplitKind.Call, invocation.Syntax, SyncScope.Block, invocation, graph)
            : new SplitPoint(SplitKind.Call, invocation.Syntax, null, null, graph);
    }

    // <arguments> <body>: the body where it is called, once for SyncGrid with a split in each gap, in a loop for
    // ForUnroll; a SyncGrid body with a gap is a grid barrier where it is called
    private void WalkMacro(IInvocationOperation invocation, GpuMacroCall macro, ControlFlowGraph graph)
    {
        IFlowAnonymousFunctionOperation? body = null;
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == macro.LambdaArgumentIndex && UnwrapLambda(argument.Value) is { } lambda)
            {
                body = lambda;
                continue;
            }

            Walk(argument.Value, graph);
        }

        foreach (var recorder in _recorders)
        {
            recorder.OnCall(invocation, _current.Index);
        }

        var gaps = macro.Kind == GpuMacroKind.SyncGrid && body?.Syntax is LambdaExpressionSyntax syntax
            ? new SyncGridGaps(SyncGridBody.Flatten(syntax, _model).Elements, _model)
            : null;
        if (gaps is { HasAnyGap: true })
        {
            var split = new SplitPoint(SplitKind.SyncGridCall, invocation.Syntax, SyncScope.Grid, null, graph);
            Record((recorder, node) => recorder.OnSplit(split, node), _current.Index);
        }

        if (body == null)
        {
            return;
        }

        var isLoop = macro.Kind == GpuMacroKind.ForUnroll;
        var spliced = new SplicedBody(SplicedBodyKind.Macro, body.Syntax, body.Symbol.Parameters, HasUniformIndex: isLoop);
        SpliceGraph(spliced, graph.GetAnonymousFunctionControlFlowGraphInScope(body), gaps, isLoop);
    }

    private static IFlowAnonymousFunctionOperation? UnwrapLambda(IOperation operation) => operation switch
    {
        IFlowAnonymousFunctionOperation lambda => lambda,
        IDelegateCreationOperation creation => UnwrapLambda(creation.Target),
        IConversionOperation conversion => UnwrapLambda(conversion.Operand),
        _ => null
    };

    // A lambda where it is created
    private void SpliceLambda(IFlowAnonymousFunctionOperation function, ControlFlowGraph graph)
    {
        var body = new SplicedBody(SplicedBodyKind.Lambda, function.Syntax, function.Symbol.Parameters);
        SpliceGraph(body, graph.GetAnonymousFunctionControlFlowGraphInScope(function), gaps: null, isLoop: false);
    }

    // A local function where it is called or converted to a delegate, once on each path (a recursive call adds nothing)
    private void SpliceLocalFunction(IMethodSymbol function, ControlFlowGraph graph, SplicedBodyKind kind)
    {
        if (!function.TryGetDeclaringSyntax(out LocalFunctionStatementSyntax? syntax) ||
            !_localFunctionsInProgress.Add(function))
        {
            return;
        }

        var body = new SplicedBody(kind, syntax, function.Parameters);
        SpliceGraph(body, graph.GetLocalFunctionControlFlowGraphInScope(function), gaps: null, isLoop: false);
        _localFunctionsInProgress.Remove(function);
    }

    // before -> <body> -> after, and after the body back to its entry for a loop
    private void SpliceGraph(SplicedBody body, ControlFlowGraph bodyGraph, SyncGridGaps? gaps, bool isLoop)
    {
        var before = _current;
        var range = _topRange;
        foreach (var recorder in _recorders)
        {
            recorder.OnBodyEntered(body, before.Index);
        }

        var (entry, exit) = Build(bodyGraph, gaps, isRoot: false);
        var after = NewNode();
        after.First = range;
        after.Last = range;
        Connect(before, entry);
        Connect(exit, after);
        if (isLoop)
        {
            Connect(exit, entry);
        }

        _current = after;
        foreach (var recorder in _recorders)
        {
            recorder.OnBodyExited(body, after.Index);
        }
    }

    private void Record(Action<IFlowRecorder, int> record, int node)
    {
        foreach (var recorder in _recorders)
        {
            record(recorder, node);
        }
    }

    private SplicedFlowNode NewNode()
    {
        var node = new SplicedFlowNode(_nodes.Count);
        _nodes.Add(node);
        foreach (var recorder in _recorders)
        {
            recorder.OnNode(node.Index);
        }

        return node;
    }

    private static void Connect(SplicedFlowNode from, SplicedFlowNode to)
    {
        if (!from.Successors.Contains(to.Index))
        {
            from.Successors.Add(to.Index);
        }
    }
}
