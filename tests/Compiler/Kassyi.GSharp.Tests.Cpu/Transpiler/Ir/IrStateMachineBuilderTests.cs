#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies <see cref="IrStateMachineBuilder"/> and <see cref="IrStateMachineInvariants.Verify"/>: two-pass state
/// numbering, empty-state preservation, barrier/shuffle/suspending-call splitting, omitted mask literal text, node
/// separation between publish and read, and removed-call handling.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrStateMachineBuilderTests
{
    private const string FrameCases = "Transpiler/Frames/StateMachineFrameSource.cs";
    private const string FrameKernelsType = "FrameCases.FrameKernels";
    private const string LinearCases = "Ir/IrLinearCases.cs";
    private const string LinearCasesType = "IrLinear.IrLinearCases";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Build_BarrierInKernel_SplitsAfterTheBarrier()
    {
        var machine = BuildMachine(FrameCases, FrameKernelsType, "SlotsKernel", isDeviceFunction: false);

        Assert.Equal(2, machine.States.Length);
        Assert.Equal(["flag block", "suspend 1"], Events(machine.States[0]));
        Assert.Equal(["exit"], Events(machine.States[1]));
        Assert.Equal(0, machine.WarpSiteCount);
        Assert.Empty(machine.CallFrames);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Build_SuspendingCallThenShuffle_NumbersFramesAndSites()
    {
        var machine = BuildMachine(FrameCases, FrameKernelsType, "CallerKernel", isDeviceFunction: false);

        Assert.Equal(3, machine.States.Length);
        Assert.Equal(["jump 1"], Events(machine.States[0]));
        Assert.Equal(["resume 0", "publish 0", "suspend 2"], Events(machine.States[1]));
        Assert.Equal(["read 0", "exit"], Events(machine.States[2]));
        Assert.Equal(1, machine.WarpSiteCount);
        Assert.Equal(["Step"], machine.CallFrames.Select(static f => f.Name).ToArray());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Build_BarrierInDeviceFunction_EndsWithReturn()
    {
        var machine = BuildMachine(FrameCases, FrameKernelsType, "Step", isDeviceFunction: true);

        Assert.Equal(2, machine.States.Length);
        Assert.Equal(["flag block", "suspend 1"], Events(machine.States[0]));
        Assert.Equal(["return", "exit"], Events(machine.States[1]));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Build_ResumeIntoDeclaredTemporary_TargetsTheDeclaration()
    {
        var machine = BuildMachine(FrameCases, FrameKernelsType, "CallerKernel", isDeviceFunction: false);

        var resume = Assert.IsType<IrResumeCall>(machine.States[1].Statements[0]);
        Assert.NotNull(resume.ResultTarget);
        Assert.True(resume.ResultTarget.IsDeclaration);
        Assert.Equal(IrVariableKind.Temporary, resume.ResultTarget.Variable.Kind);
        Assert.Equal(1, resume.State);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Build_OmittedMask_WritesTheHexLiteral()
    {
        var machine = BuildMachine(LinearCases, LinearCasesType, "SyncWarpDefault", isDeviceFunction: false);

        var (publish, read) = SinglePublishAndRead(machine);
        var publishMask = Assert.IsType<IrConstant>(publish.Arguments[0].Value);
        var readMask = Assert.IsType<IrConstant>(read.Arguments[0].Value);

        Assert.Equal("0xFFFFFFFFu", publishMask.LiteralText);
        Assert.Equal(0xFFFFFFFFu, publishMask.Value);
        Assert.Equal("0xFFFFFFFFu", readMask.LiteralText);
        Assert.Equal(0xFFFFFFFFu, readMask.Value);
        Assert.NotSame(publishMask, readMask);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Build_PublishAndRead_ShareNoNode()
    {
        var machine = BuildMachine(FrameCases, FrameKernelsType, "CallerKernel", isDeviceFunction: false);

        var (publish, read) = SinglePublishAndRead(machine);
        var publishNodes = new HashSet<IrNode>(
            publish.Arguments.SelectMany(static a => IrNodeChildren.DescendantsAndSelf(a.Value)),
            ReferenceEqualityComparer.Instance);
        var readNodes = read.Arguments
            .SelectMany(static a => IrNodeChildren.DescendantsAndSelf(a.Value));

        Assert.DoesNotContain(readNodes, publishNodes.Contains);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Build_ConstantExpressionMask_ShareNoNode()
    {
        var machine = BuildMachine(LinearCases, LinearCasesType, "NotMask", isDeviceFunction: false);

        var (publish, read) = SinglePublishAndRead(machine);
        var publishMask = Assert.IsType<IrUnary>(publish.Arguments[0].Value);
        var readMask = Assert.IsType<IrUnary>(read.Arguments[0].Value);

        Assert.NotSame(publishMask, readMask);
        Assert.NotSame(publishMask.Operand, readMask.Operand);
        Assert.IsType<IrConstant>(publishMask.Operand);
        Assert.IsType<IrConstant>(readMask.Operand);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Build_RemovedCallStatement_IsNotASplit()
    {
        var machine = BuildMachine(LinearCases, LinearCasesType, "RemovedCallStatement", isDeviceFunction: false);

        Assert.Equal(2, machine.States.Length);
        Assert.Equal(["flag block", "suspend 1"], Events(machine.States[0]));
        Assert.Equal(["exit"], Events(machine.States[1]));
        Assert.Equal(0, machine.WarpSiteCount);
        Assert.Contains(machine.States[0].Statements, static s => s is IrExpressionStatement);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Build_LabelAfterBarrier_KeepsTheEmptyState()
    {
        var machine = BuildMachine(LinearCases, LinearCasesType, "Numbering", isDeviceFunction: false);

        Assert.Equal(9, machine.States.Length);
        Assert.Equal(["jump 2"], Events(machine.States[1]));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Build_WhileLoop_JumpsBackToTheStartState()
    {
        var machine = BuildMachine(LinearCases, LinearCasesType, "WhileBarrier", isDeviceFunction: false);

        Assert.Equal(5, machine.States.Length);
        Assert.Equal(["jump 1"], Events(machine.States[0]));
        Assert.Equal(["jump-if-false 4", "flag block", "suspend 2"], Events(machine.States[1]));
        var branch = Assert.IsType<IrStateJump>(machine.States[1].Statements[0]);
        Assert.False(branch.JumpIfTrue);
        Assert.Equal(["jump 3"], Events(machine.States[2]));
        Assert.Equal(["jump 1", "jump 4"], Events(machine.States[3]));
        Assert.Equal(["exit"], Events(machine.States[4]));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Build_ConditionalGotoWithJumpIfTrue_PreservesPolarity()
    {
        var compilation = IrTestCases.Compile(LinearCases);
        var built = IrTestCases.Build(LinearCases, LinearCasesType, "NoSplitIf");
        var source = built.Body.Source;
        var condition = new IrConstant(true, null, compilation.GetSpecialType(SpecialType.System_Boolean), source);
        var target = new IrLabelSymbol(IrLabelKind.BranchEnd, 0);
        var linear = ImmutableArray.Create<IrStatement>(
            new IrConditionalGoto(condition, jumpIfTrue: true, target, source),
            new IrLabel(target, source));

        var machine = IrStateMachineBuilder.Build(linear, isDeviceFunction: false, source, compilation);

        Assert.Empty(IrStateMachineInvariants.Verify(machine));
        Assert.Equal(["jump-if-true 1", "jump 1"], Events(machine.States[0]));
        var jump = Assert.IsType<IrStateJump>(machine.States[0].Statements[0]);
        Assert.True(jump.JumpIfTrue);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void VerifyStateMachine_NestedSuspendOrExit_ReportsViolation()
    {
        var compilation = IrTestCases.Compile(LinearCases);
        var built = IrTestCases.Build(LinearCases, LinearCasesType, "NoSplitIf");
        var source = built.Body.Source;
        var condition = new IrConstant(true, null, compilation.GetSpecialType(SpecialType.System_Boolean), source);

        var nestedSuspend = new IrStateMachine(
            [
                new IrState(0, [
                    new IrIf(condition, new IrSuspend(IrSuspendKind.BlockBarrier, 1, source), null, source),
                    new IrStateJump(null, false, 1, source)
                ], null, source),
                new IrState(1, [new IrStateMachineExit(source)], null, source)
            ],
            isDeviceFunction: false,
            warpSiteCount: 0,
            callFrames: ImmutableArray<IMethodSymbol>.Empty,
            source);
        var nestedExit = new IrStateMachine(
            [
                new IrState(0, [
                    new IrIf(condition, new IrStateMachineExit(source), null, source),
                    new IrStateMachineExit(source)
                ], null, source)
            ],
            isDeviceFunction: false,
            warpSiteCount: 0,
            callFrames: ImmutableArray<IMethodSymbol>.Empty,
            source);

        Assert.Contains(IrStateMachineInvariants.Verify(nestedSuspend),
            static v => v.StartsWith("SM2 ", StringComparison.Ordinal));
        Assert.Contains(IrStateMachineInvariants.Verify(nestedExit),
            static v => v.StartsWith("SM2 ", StringComparison.Ordinal));
    }

    private static IrStateMachine BuildMachine(string testCase, string typeName, string methodName,
        bool isDeviceFunction)
    {
        var compilation = IrTestCases.Compile(testCase);
        var built = IrTestCases.Build(testCase, typeName, methodName);
        var classified = IrClassifier.Classify(built, compilation);
        var lowered = IrSplitLowering.Apply(classified, compilation, out var failure);
        Assert.NotNull(lowered);
        Assert.Equal(IrSplitLoweringFailureKind.None, failure.Kind);
        var linear = IrLinearizer.Linearize(lowered, compilation);
        var machine = IrStateMachineBuilder.Build(linear, isDeviceFunction, built.Body.Source, compilation);
        Assert.Empty(IrStateMachineInvariants.Verify(machine));
        return machine;
    }

    private static (IrRuntimeCall Publish, IrRuntimeCall Read) SinglePublishAndRead(IrStateMachine machine)
    {
        var runtimeCalls = IrNodeChildren.DescendantsAndSelf(machine)
            .OfType<IrRuntimeCall>()
            .ToArray();
        var publish = Assert.Single(runtimeCalls, static c => c.Function == IrRuntimeFunction.WarpPublish);
        var read = Assert.Single(runtimeCalls, static c => c.Function == IrRuntimeFunction.WarpRead);
        return (publish, read);
    }

    private static string[] Events(IrState state)
    {
        var events = new List<string>();
        foreach (var statement in state.Statements)
        {
            switch (statement)
            {
                case IrStateJump { Condition: null } jump:
                    events.Add($"jump {jump.State}");
                    break;
                case IrStateJump { Condition: not null } jump:
                    events.Add(jump.JumpIfTrue ? $"jump-if-true {jump.State}" : $"jump-if-false {jump.State}");
                    break;
                case IrSuspend { Kind: IrSuspendKind.BlockBarrier } suspend:
                    events.Add("flag block");
                    events.Add($"suspend {suspend.ResumeState}");
                    break;
                case IrSuspend { Kind: IrSuspendKind.GridBarrier } suspend:
                    events.Add("flag grid");
                    events.Add($"suspend {suspend.ResumeState}");
                    break;
                case IrSuspend { Kind: IrSuspendKind.WarpPublish } suspend:
                    events.Add($"suspend {suspend.ResumeState}");
                    break;
                case IrResumeCall resume:
                    events.Add($"resume {resume.CallFrame}");
                    break;
                case IrExpressionStatement
                {
                    Expression: IrRuntimeCall { Function: IrRuntimeFunction.WarpPublish } pub
                }:
                    events.Add($"publish {pub.Site}");
                    break;
                case var s when IrNodeChildren.DescendantsAndSelf(s)
                    .OfType<IrRuntimeCall>()
                    .FirstOrDefault(static c => c.Function == IrRuntimeFunction.WarpRead) is { } read:
                    events.Add($"read {read.Site}");
                    break;
                case IrReturn:
                    events.Add("return");
                    break;
                case IrStateMachineExit:
                    events.Add("exit");
                    break;
            }
        }

        return events.ToArray();
    }
}
