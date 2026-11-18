#nullable enable
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Frames;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis.CSharp;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies the gen/kill combinators (<see cref="IrTransfer.Then"/>, <see cref="IrTransfer.Either"/>,
/// <see cref="IrTransfer.Loop"/>) and node transfer evaluation (<see cref="IrLivenessTransfer"/>) on minimal
/// hand-built IR nodes.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrLivenessTransferTests
{
    private const string LivenessCases = "Ir/IrLivenessCases.cs";
    private const string LivenessCasesType = "IrLiveness.IrLivenessCases";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Then_ReadAfterWrite_DoesNotGen()
    {
        var a = 0;
        var transfer = IrTransfer.Then(IrTransfer.Write(a, 1), IrTransfer.Read(a, 1));

        Assert.False(transfer.HasGen(a));
        Assert.True(transfer.HasKill(a));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Either_WriteInOneArm_DoesNotKill()
    {
        var a = 0;
        var empty = IrTransfer.Empty(1);
        var transfer = IrTransfer.Either(empty, IrTransfer.Write(a, 1), empty);

        Assert.False(transfer.HasKill(a));
        Assert.False(transfer.HasGen(a));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Loop_WriteInBody_KillsOnlyTheCondition()
    {
        var a = 0;
        var b = 1;
        var transfer = IrTransfer.Loop(IrTransfer.Read(b, 2), IrTransfer.Write(a, 2));

        Assert.False(transfer.HasKill(a));
        Assert.False(transfer.HasKill(b));
        Assert.True(transfer.HasGen(b));
        Assert.False(transfer.HasGen(a));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void OfValue_OpaqueExpression_Throws()
    {
        var compilation = IrTestCases.Compile(LivenessCases);
        var built = IrTestCases.Build(LivenessCases, LivenessCasesType, "NoSplit");
        var intType = compilation.GetSpecialType(SpecialType.System_Int32);
        var opaque = new IrOpaqueExpression(
            SyntaxFactory.IdentifierName("x"),
            null,
            IrOpaqueReason.UnsupportedOperation,
            IrOpaqueSplit.None,
            intType,
            IrCategory.Value,
            built.Body.Source);

        Assert.Throws<InvalidOperationException>(() =>
            IrLivenessTransfer.OfValue(opaque, new Dictionary<IrVariable, int>(), 0));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void OfValue_CallWithOutArgument_WritesAfterTheArguments()
    {
        var compilation = IrTestCases.Compile(LivenessCases);
        var built = IrTestCases.Build(LivenessCases, LivenessCasesType, "NoSplit");
        var source = built.Body.Source;
        var intType = compilation.GetSpecialType(SpecialType.System_Int32);
        var voidType = compilation.GetSpecialType(SpecialType.System_Void);
        var readMethod = IrTestCases.FindMethod(compilation, LivenessCasesType, "Read");
        var x = IrVariable.CreateTemporary(intType, RefKind.None, "x", source);
        var arg0 = new IrArgument(
            readMethod.Parameters[0],
            new IrVariableReference(x, false, IrCategory.Value, default, source),
            IrArgumentKind.Explicit,
            0);
        var arg1 = new IrArgument(
            readMethod.Parameters[1],
            new IrVariableReference(x, false, IrCategory.Location, default, source),
            IrArgumentKind.Explicit,
            1);
        var call = new IrCall(
            readMethod,
            null,
            [arg0, arg1],
            IrCallClassification.Of(IrSplit.None, DeviceFunctionEffect.None),
            voidType,
            IrCategory.Value,
            source);
        var indices = new Dictionary<IrVariable, int> { [x] = 0 };

        var transfer = IrLivenessTransfer.OfValue(call, indices, 1);

        Assert.True(transfer.HasGen(0));
        Assert.True(transfer.HasKill(0));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void OfResumeCall_WithRefResultTarget_WritesResultTarget()
    {
        var compilation = IrTestCases.Compile(LivenessCases);
        var built = IrTestCases.Build(LivenessCases, LivenessCasesType, "NoSplit");
        var source = built.Body.Source;
        var intType = compilation.GetSpecialType(SpecialType.System_Int32);
        var sumMethod = IrTestCases.FindMethod(compilation, LivenessCasesType, "Sum");
        var arg = new IrArgument(
            sumMethod.Parameters[0],
            new IrConstant(null, null, sumMethod.Parameters[0].Type, source),
            IrArgumentKind.Explicit,
            0);
        var call = new IrCall(
            sumMethod,
            null,
            [arg],
            IrCallClassification.Of(IrSplit.SuspendingCall, DeviceFunctionEffect.None),
            intType,
            IrCategory.Location,
            source);
        var targetVar = IrVariable.CreateTemporary(intType, RefKind.Ref, "t", source);
        var targetRef = new IrVariableReference(targetVar, true, IrCategory.Location, default, source);
        var resume = new IrResumeCall(call, 0, targetRef, 1, source);
        var indices = new Dictionary<IrVariable, int> { [targetVar] = 0 };

        var (read, write) = IrLivenessTransfer.OfResumeCall(resume, indices, 1);

        Assert.False(read.HasGen(0));
        Assert.False(read.HasKill(0));
        Assert.True(write.HasKill(0));
        Assert.False(write.HasGen(0));

        var writeTarget = new IrExpressionStatement(
            new IrAssignment(
                new IrVariableReference(targetVar, false, IrCategory.Location, default, source),
                new IrConstant(1, null, intType, source),
                false,
                intType,
                IrCategory.Value,
                source),
            source);
        var state0 = new IrState(0, [new IrStateJump(null, true, 1, source)], null, source);
        var state1 = new IrState(1, [resume, writeTarget, new IrStateMachineExit(source)], null, source);
        var machine = new IrStateMachine([state0, state1], false, 0, [sumMethod], source);

        Assert.Empty(IrStateMachineInvariants.Verify(machine));
        var liveness = IrLivenessAnalysis.Analyze(machine);
        Assert.Empty(IrLivenessInvariants.Verify(machine, liveness));
        var target = liveness[targetVar];
        Assert.Equal(IrDeclarationForm.ResumeResult, target.Form);
        Assert.False(liveness.IsLiveAcrossSourceSplit(target));
        Assert.False(liveness.IsLiveAcrossAnySplit(target));
        Assert.False(liveness.IsTouchedInAnotherState(target));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Analyze_MismatchedOrDuplicateStateNumbers_ThrowsInvalidOperationException()
    {
        var compilation = IrTestCases.Compile(LivenessCases);
        var built = IrTestCases.Build(LivenessCases, LivenessCasesType, "NoSplit");
        var source = built.Body.Source;
        var intType = compilation.GetSpecialType(SpecialType.System_Int32);
        var exprStmt = new IrExpressionStatement(new IrConstant(0, null, intType, source), source);
        var suspend = new IrSuspend(IrSuspendKind.BlockBarrier, 1, source);
        var exit = new IrStateMachineExit(source);

        var dupZeroWithBadEnd = new IrStateMachine(
            [new IrState(0, [exprStmt, suspend], null, source), new IrState(0, [exprStmt], null, source)],
            false, 0, [], source);
        var dupOneSplit = new IrStateMachine(
            [new IrState(1, [suspend], null, source), new IrState(1, [exit], null, source)],
            false, 0, [], source);
        var swappedNoSplit = new IrStateMachine(
            [new IrState(1, [new IrStateJump(null, true, 0, source)], null, source),
                new IrState(0, [exit], null, source)],
            false, 0, [], source);
        var dupZeroNoSplit = new IrStateMachine(
            [new IrState(0, [new IrStateJump(null, true, 1, source)], null, source),
                new IrState(0, [exit], null, source)],
            false, 0, [], source);
        var emptyMachine = new IrStateMachine([], false, 0, [], source);

        Assert.Throws<InvalidOperationException>(() => IrLivenessAnalysis.Analyze(dupZeroWithBadEnd));
        Assert.Throws<InvalidOperationException>(() => IrLivenessGraph.Build(dupZeroWithBadEnd));
        Assert.Throws<InvalidOperationException>(() => IrLivenessAnalysis.Analyze(dupOneSplit));
        Assert.Throws<InvalidOperationException>(() => IrLivenessGraph.Build(dupOneSplit));
        Assert.Throws<InvalidOperationException>(() => IrLivenessAnalysis.Analyze(swappedNoSplit));
        Assert.Throws<InvalidOperationException>(() => IrLivenessAnalysis.Analyze(dupZeroNoSplit));

        var emptyLiveness = IrLivenessAnalysis.Analyze(emptyMachine);
        Assert.False(emptyLiveness.HasSplit);
        Assert.Empty(emptyLiveness.Variables);
        Assert.Empty(IrLivenessInvariants.Verify(emptyMachine, emptyLiveness));
        Assert.Equal(0, IrLivenessGraph.Build(emptyMachine).NodeCount);
    }
}
