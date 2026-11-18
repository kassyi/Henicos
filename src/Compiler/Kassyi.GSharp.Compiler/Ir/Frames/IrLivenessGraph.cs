using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Frames;

/// <summary>
/// The control-flow graph of an <see cref="IrStateMachine"/>: one node per top-level statement in
/// <see cref="IrStatePosition"/> order, its successor edges, and the split-entry nodes.
/// </summary>
internal sealed class IrLivenessGraph
{
    private readonly IrStatePosition[] _positions;
    private readonly IrStatement[] _statements;
    private readonly int[][] _successors;
    private readonly bool[] _sourceSplitEntries;
    private readonly bool[] _anySplitEntries;

    private IrLivenessGraph(IrStatePosition[] positions, IrStatement[] statements, int[][] successors,
        bool[] sourceSplitEntries, bool[] anySplitEntries)
    {
        _positions = positions;
        _statements = statements;
        _successors = successors;
        _sourceSplitEntries = sourceSplitEntries;
        _anySplitEntries = anySplitEntries;
    }

    /// <summary>The total number of top-level statement nodes.</summary>
    public int NodeCount => _statements.Length;

    /// <summary>Builds the flow graph for <paramref name="machine"/>, validating the state-end rule.</summary>
    public static IrLivenessGraph Build(IrStateMachine machine)
    {
        var (positions, statements, stateFirstNode) = CollectNodes(machine);
        var nodeCount = statements.Length;
        var successors = new int[nodeCount][];
        var sourceSplitEntries = new bool[nodeCount];
        var anySplitEntries = new bool[nodeCount];
        PopulateEdges(machine, positions, statements, stateFirstNode, successors, sourceSplitEntries, anySplitEntries);
        return new IrLivenessGraph(positions, statements, successors, sourceSplitEntries, anySplitEntries);
    }

    /// <summary>The state and statement index of node <paramref name="nodeIndex"/>.</summary>
    public IrStatePosition PositionOf(int nodeIndex) => _positions[nodeIndex];

    /// <summary>The top-level statement at node <paramref name="nodeIndex"/>.</summary>
    public IrStatement StatementOf(int nodeIndex) => _statements[nodeIndex];

    /// <summary>The successor node indices of node <paramref name="nodeIndex"/>.</summary>
    public int[] SuccessorsOf(int nodeIndex) => _successors[nodeIndex];

    /// <summary>Whether node <paramref name="nodeIndex"/> is a non-reconvergence split entry.</summary>
    public bool IsSourceSplitEntry(int nodeIndex) => _sourceSplitEntries[nodeIndex];

    /// <summary>Whether node <paramref name="nodeIndex"/> is any split entry.</summary>
    public bool IsAnySplitEntry(int nodeIndex) => _anySplitEntries[nodeIndex];

    /// <summary>
    /// Whether node <paramref name="nodeIndex"/> is immediately before a split (<see cref="IrSuspend"/> or the
    /// <see cref="IrResumeCall"/> starting a state).
    /// </summary>
    public bool IsSplitTrigger(int nodeIndex) =>
        _statements[nodeIndex] is IrSuspend ||
        (_statements[nodeIndex] is IrResumeCall && _positions[nodeIndex].Index == 0);

    private static (IrStatePosition[] Positions, IrStatement[] Statements, int[] StateFirstNode) CollectNodes(
        IrStateMachine machine)
    {
        var total = 0;
        var stateFirstNode = new int[machine.States.Length];
        for (var s = 0; s < machine.States.Length; s++)
        {
            var state = machine.States[s];
            if (state.Number != s)
            {
                throw new InvalidOperationException(
                    $"State number {state.Number} does not match state table index {s}.");
            }

            if (state.Statements.IsEmpty)
            {
                throw new InvalidOperationException($"State {state.Number} has no statements.");
            }

            stateFirstNode[s] = total;
            total += state.Statements.Length;
        }

        var positions = new IrStatePosition[total];
        var statements = new IrStatement[total];
        var cursor = 0;
        for (var s = 0; s < machine.States.Length; s++)
        {
            var state = machine.States[s];
            for (var i = 0; i < state.Statements.Length; i++)
            {
                positions[cursor] = new IrStatePosition(state.Number, i);
                statements[cursor] = state.Statements[i];
                cursor++;
            }
        }

        return (positions, statements, stateFirstNode);
    }

    private static void PopulateEdges(IrStateMachine machine, IrStatePosition[] positions, IrStatement[] statements,
        int[] stateFirstNode, int[][] successors, bool[] sourceSplitEntries, bool[] anySplitEntries)
    {
        for (var nodeIndex = 0; nodeIndex < statements.Length; nodeIndex++)
        {
            var pos = positions[nodeIndex];
            var state = machine.States[pos.State];
            var isLast = pos.Index == state.Statements.Length - 1;
            var statement = statements[nodeIndex];
            if (isLast && statement is not (IrSuspend or IrStateJump { Condition: null } or IrReturn
                    or IrStateMachineExit))
            {
                throw new InvalidOperationException(
                    $"State {state.Number} must end with suspend, unconditional jump, return, or exit, " +
                    $"not {statement.GetType().Name}.");
            }

            successors[nodeIndex] = ComputeSuccessors(
                statement, nodeIndex, pos, stateFirstNode, sourceSplitEntries, anySplitEntries);
        }
    }

    private static int[] ComputeSuccessors(IrStatement statement, int nodeIndex, IrStatePosition position,
        int[] stateFirstNode, bool[] sourceSplitEntries, bool[] anySplitEntries)
    {
        switch (statement)
        {
            case IrStateJump { Condition: null } unconditional:
                return [TargetFirstNode(stateFirstNode, unconditional.State)];
            case IrStateJump conditional:
                return [TargetFirstNode(stateFirstNode, conditional.State), nodeIndex + 1];
            case IrSuspend suspend:
                var target = TargetFirstNode(stateFirstNode, suspend.ResumeState);
                anySplitEntries[target] = true;
                if (suspend.Kind != IrSuspendKind.Reconverge)
                {
                    sourceSplitEntries[target] = true;
                }

                return [target];
            case IrReturn or IrStateMachineExit:
                return [];
            case IrResumeCall:
                if (position.Index == 0)
                {
                    anySplitEntries[nodeIndex] = true;
                    sourceSplitEntries[nodeIndex] = true;
                }

                return [nodeIndex + 1];
            default:
                return [nodeIndex + 1];
        }
    }

    private static int TargetFirstNode(int[] stateFirstNode, int state) =>
        (uint)state < (uint)stateFirstNode.Length
            ? stateFirstNode[state]
            : throw new InvalidOperationException($"State target {state} is out of range.");
}
