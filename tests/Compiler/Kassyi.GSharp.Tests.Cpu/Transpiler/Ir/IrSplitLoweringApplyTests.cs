#nullable enable
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies <see cref="IrSplitLowering"/> and <see cref="IrSpillRewriter.TryApplyLoopCondition"/>: failure precedence,
/// rebuilt <see cref="IrWhile"/> keys, comment preservation, and guard exceptions.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrSplitLoweringApplyTests
{
    private const string Cases = "Ir/IrLinearCases.cs";
    private const string CasesType = "IrLinear.IrLinearCases";

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Apply_ForLoopWithBarrier_FailsWithTheOpaqueFallback()
    {
        var compilation = IrTestCases.Compile(Cases);
        var classified = IrClassifier.Classify(IrTestCases.Build(Cases, CasesType, "ForWithBarrier"), compilation);

        var lowered = IrSplitLowering.Apply(classified, compilation, out var failure);

        Assert.Null(lowered);
        Assert.Equal(IrSplitLoweringFailureKind.Fallback, failure.Kind);
        Assert.Equal(IrSpillFallback.OpaqueHoldsSplit, failure.Fallback);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Apply_BreakInLoop_FailsWithAnOpaqueStatement()
    {
        var compilation = IrTestCases.Compile(Cases);
        var classified = IrClassifier.Classify(IrTestCases.Build(Cases, CasesType, "BreakInLoop"), compilation);

        var lowered = IrSplitLowering.Apply(classified, compilation, out var failure);

        Assert.Null(lowered);
        Assert.Equal(IrSplitLoweringFailureKind.OpaqueStatement, failure.Kind);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Apply_WhileConditionWithSplit_RecordsTheLoweredConditionByTheRebuiltLoop()
    {
        var compilation = IrTestCases.Compile(Cases);
        var classified = IrClassifier.Classify(
            IrTestCases.Build(Cases, CasesType, "ShuffleConditionWithBodySplit"), compilation);
        var originalWhile = Assert.IsType<IrWhile>(classified.Body.Statements[0]);

        var lowered = IrSplitLowering.Apply(classified, compilation, out var failure);

        Assert.NotNull(lowered);
        Assert.Equal(IrSplitLoweringFailureKind.None, failure.Kind);
        var rebuiltWhile = Assert.IsType<IrWhile>(lowered.Body.Statements[0]);
        Assert.NotSame(originalWhile, rebuiltWhile);
        Assert.True(lowered.LoopConditions.ContainsKey(rebuiltWhile));
        Assert.False(lowered.LoopConditions.ContainsKey(originalWhile));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ApplyLoopCondition_StatementThatIsNotALoop_Throws()
    {
        var compilation = IrTestCases.Compile(Cases);
        var classified = IrClassifier.Classify(IrTestCases.Build(Cases, CasesType, "Commented"), compilation);
        var plan = IrSpillPlanner.Plan(classified, compilation);

        Assert.Throws<ArgumentException>(() =>
            IrSpillRewriter.TryApplyLoopCondition(plan.Statements[0], out _, out _));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Apply_StatementWithComments_KeepsThemOnTheFirstStatementOnly()
    {
        var compilation = IrTestCases.Compile(Cases);
        var classified = IrClassifier.Classify(IrTestCases.Build(Cases, CasesType, "Commented"), compilation);

        var lowered = IrSplitLowering.Apply(classified, compilation, out var failure);

        Assert.NotNull(lowered);
        Assert.Equal(IrSplitLoweringFailureKind.None, failure.Kind);
        var block = Assert.IsType<IrBlock>(lowered.Body.Statements[0]);
        Assert.True(block.Statements.Length > 1);
        Assert.Equal("// note", Assert.Single(block.Statements[0].CommentsBefore));
        for (var i = 1; i < block.Statements.Length; i++)
        {
            Assert.Empty(block.Statements[i].CommentsBefore);
        }
    }
}
