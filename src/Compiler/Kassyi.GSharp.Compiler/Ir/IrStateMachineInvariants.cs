using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>
/// Checks the invariants of the state-machine form that <see cref="IrStateMachineBuilder"/> makes, for tests and for
/// the passes that rewrite it.
/// </summary>
/// <remarks>
/// Each violation is one line that starts with the number of the invariant (<c>SM2 at 10..20: ...</c>): SM1 the states
/// are numbered from 0 in order, SM2 a state ends with a suspend, an unconditional jump, a return or the exit, and only
/// there; SM3 no linear statement is left; SM4 a jump targets an existing state other than 0; SM5 a suspend and the
/// trailing jump go to the next state; SM6 a resuming call starts its state, uses each call frame once and declares a
/// temporary; SM7 the warp sites are in range and a publish and its read share no node; SM8 nothing of the
/// reconvergence pass is there yet.
/// </remarks>
internal sealed class IrStateMachineInvariants
{
    private readonly IrStateMachine _machine;
    private readonly ImmutableArray<string>.Builder _violations = ImmutableArray.CreateBuilder<string>();
    private readonly HashSet<int> _seenFrames = [];
    private readonly HashSet<IrNode> _publishNodes = [];
    private readonly ImmutableArray<IrRuntimeCall>.Builder _readCalls = ImmutableArray.CreateBuilder<IrRuntimeCall>();

    private IrStateMachineInvariants(IrStateMachine machine) => _machine = machine;

    /// <summary>The violations of the invariants in <paramref name="machine"/>; empty when it keeps them all.</summary>
    public static ImmutableArray<string> Verify(IrStateMachine machine)
    {
        var checker = new IrStateMachineInvariants(machine);
        if (machine.States.IsEmpty)
        {
            checker.Add("SM1", machine.Source, "state machine has no states");
            return checker._violations.ToImmutable();
        }

        if (machine.WarpSiteCount < 0)
        {
            checker.Add("SM7", machine.Source, "warp site count is negative");
        }

        for (var index = 0; index < machine.States.Length; index++)
        {
            checker.CheckState(machine.States[index], index);
        }

        if (checker._seenFrames.Count != machine.CallFrames.Length)
        {
            checker.Add("SM6", machine.Source,
                $"call frame count {checker._seenFrames.Count} does not match callFrames length " +
                $"{machine.CallFrames.Length}");
        }

        checker.CheckSharedWarpNodes();
        return checker._violations.ToImmutable();
    }

    private void CheckState(IrState state, int index)
    {
        if (state.Number != index)
        {
            Add("SM1", state.Source, $"state number {state.Number} does not match expected index {index}");
        }

        if (state.UnreachableFrom is not null)
        {
            Add("SM8", state.Source, "unreachableFrom must be null before the unreachable-statement pass");
        }

        if (state.Statements.IsEmpty)
        {
            Add("SM2", state.Source, $"state {state.Number} has no statements");
            return;
        }

        CheckEnd(state);
        for (var position = 0; position < state.Statements.Length; position++)
        {
            CheckTopLevel(state, state.Statements[position], position);
            CheckNodes(state.Statements[position], position);
        }
    }

    private void CheckEnd(IrState state)
    {
        var last = state.Statements[^1];
        if (last is not (IrSuspend or IrStateJump { Condition: null } or IrReturn or IrStateMachineExit))
        {
            Add("SM2", last.Source,
                $"state {state.Number} must end with suspend, unconditional jump, return, or exit, " +
                $"not {last.GetType().Name}");
        }

        if (last is IrStateJump { Condition: null } trailingJump && trailingJump.State != state.Number + 1)
        {
            Add("SM5", trailingJump.Source,
                $"trailing unconditional jump targets state {trailingJump.State}, expected {state.Number + 1}");
        }
    }

    private void CheckTopLevel(IrState state, IrStatement statement, int position)
    {
        if (position < state.Statements.Length - 1 && statement is IrSuspend or IrStateMachineExit)
        {
            Add("SM2", statement.Source,
                $"{statement.GetType().Name} may only appear as the last statement of a state");
        }

        switch (statement)
        {
            case IrSuspend suspend:
                CheckSuspend(state, suspend);
                break;
            case IrResumeCall resume:
                CheckResumeCall(state, resume, position);
                break;
        }
    }

    private void CheckSuspend(IrState state, IrSuspend suspend)
    {
        if (suspend.Kind == IrSuspendKind.Reconverge)
        {
            Add("SM8", suspend.Source, "reconverge suspend is not allowed before the reconvergence pass");
        }

        if (suspend.ResumeState != state.Number + 1 || suspend.ResumeState >= _machine.States.Length)
        {
            Add("SM5", suspend.Source,
                $"suspend resume state {suspend.ResumeState} does not match {state.Number + 1}");
        }
    }

    private void CheckResumeCall(IrState state, IrResumeCall resume, int position)
    {
        if (position != 0)
        {
            Add("SM6", resume.Source, "resume call must be the first statement of its state");
        }

        if (resume.State != state.Number)
        {
            Add("SM6", resume.Source,
                $"resume call state {resume.State} does not match containing state {state.Number}");
        }

        if (resume.CallFrame < 0 || resume.CallFrame >= _machine.CallFrames.Length ||
            !_seenFrames.Add(resume.CallFrame))
        {
            Add("SM6", resume.Source, $"resume call frame {resume.CallFrame} is out of range or duplicated");
        }

        if (resume.ResultTarget is { } target &&
            (!target.IsDeclaration || target.Variable.Kind != IrVariableKind.Temporary))
        {
            Add("SM6", resume.Source, "resume call result target must declare a temporary variable");
        }
    }

    // The nodes below a top-level statement: what may only be at the top, jump targets and the warp runtime calls
    private void CheckNodes(IrStatement statement, int position)
    {
        foreach (var node in IrNodeChildren.DescendantsAndSelf(statement))
        {
            switch (node)
            {
                case IrLabel or IrGoto or IrConditionalGoto or IrOpaqueStatement:
                    Add("SM3", node.Source, $"{node.GetType().Name} is not allowed in the state-machine form");
                    break;
                case IrStateJump jump when jump.State <= 0 || jump.State >= _machine.States.Length:
                    Add("SM4", jump.Source, $"state jump target {jump.State} is invalid or targets state 0");
                    break;
                case IrResumeCall when node != statement || position != 0:
                    Add("SM6", node.Source, "resume call must be the first top-level statement of its state");
                    break;
                case IrSuspend or IrStateMachineExit when node != statement:
                    Add("SM2", node.Source, $"{node.GetType().Name} may only appear as a top-level statement");
                    break;
                case IrRuntimeCall runtime:
                    CheckRuntimeCall(runtime);
                    break;
            }
        }
    }

    private void CheckRuntimeCall(IrRuntimeCall runtime)
    {
        if (runtime.Function is not (IrRuntimeFunction.WarpPublish or IrRuntimeFunction.WarpRead))
        {
            Add("SM8", runtime.Source,
                $"runtime function {runtime.Function.ToString()} is not allowed before reconvergence");
            return;
        }

        if (runtime.Shuffle == Analysis.WarpShuffleKind.None)
        {
            Add("SM7", runtime.Source, "warp runtime call has WarpShuffleKind.None");
        }

        if (runtime.Place is null)
        {
            Add("SM7", runtime.Source, "warp runtime call has a null place");
        }

        if (runtime.Site < 0 || runtime.Site >= _machine.WarpSiteCount)
        {
            Add("SM7", runtime.Source,
                $"warp runtime call site {runtime.Site} is outside 0..{_machine.WarpSiteCount - 1}");
        }

        if (runtime.Function == IrRuntimeFunction.WarpRead)
        {
            _readCalls.Add(runtime);
            return;
        }

        foreach (var argument in runtime.Arguments)
        {
            _publishNodes.UnionWith(IrNodeChildren.DescendantsAndSelf(argument.Value));
        }
    }

    // A publish and a read of the same collective each own their argument nodes
    private void CheckSharedWarpNodes()
    {
        foreach (var read in _readCalls)
        {
            foreach (var argument in read.Arguments)
            {
                foreach (var node in IrNodeChildren.DescendantsAndSelf(argument.Value))
                {
                    if (_publishNodes.Contains(node))
                    {
                        Add("SM7", node.Source, "warp publish and warp read share an IR node instance");
                    }
                }
            }
        }
    }

    private void Add(string code, IrSource source, string text) =>
        _violations.Add($"{code} at {source.Start}..{source.Start + source.Length}: {text}");
}
