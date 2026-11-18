#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Reconverging;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

namespace Kassyi.GSharp.Tests.Cpu.Transpiler.Ir;

/// <summary>Verifies <see cref="IrRegionInserter"/> and <see cref="IrReconvergence"/>.</summary>
[Category(TestCategory.Compiler)]
public class IrRegionInserterTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Insert_ThreadVariantIf_WrapsTheIfInARegion()
    {
        var steps = Run("ThreadVariantIf");

        Assert.Equal(["enter 0", "if(", ")", "arrive 0"], TokensOf(steps.Reconverged));
        Assert.Equal(1, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Insert_ThreadVariantIfElse_WrapsTheWholeIf()
    {
        var steps = Run("ThreadVariantIfElse");

        Assert.Equal(["enter 0", "if(", "else", ")", "arrive 0"], TokensOf(steps.Reconverged));
        Assert.Equal(1, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Insert_UniformIf_AddsNoRegion()
    {
        var steps = Run("UniformIf");

        Assert.Equal(["if(", ")", "if(", ")"], TokensOf(steps.Reconverged));
        Assert.Equal(0, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Insert_UniformBranchInADeviceFunction_AddsARegionBecauseParametersAreThreadDependent()
    {
        var steps = Run("UniformIf", isDeviceFunction: true);

        Assert.Equal(["enter 0", "if(", ")", "arrive 0", "if(", ")"], TokensOf(steps.Reconverged));
        Assert.Equal(1, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_IfAsLastStatementOfAKernel_AddsNoRegion()
    {
        var steps = Run("IfAsLastStatement");

        Assert.Equal(["if(", ")"], TokensOf(steps.Reconverged));
        Assert.Equal(0, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_IfAsLastStatementOfADeviceFunction_AddsARegion()
    {
        var steps = Run("IfAsLastStatementDevice", isDeviceFunction: true);

        Assert.Equal(["enter 0", "if(", ")", "arrive 0"], TokensOf(steps.Reconverged));
        Assert.Equal(1, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Insert_ThreadVariantWhile_WrapsTheLoop()
    {
        var steps = Run("ThreadVariantWhile");

        Assert.Equal(["enter 0", "while(", ")", "arrive 0"], TokensOf(steps.Reconverged));
        Assert.Equal(1, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Insert_UniformWhile_AddsNoRegion()
    {
        var steps = Run("UniformWhile");

        Assert.Equal(["while(", ")"], TokensOf(steps.Reconverged));
        Assert.Equal(0, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_UniformWhileWithReturn_WrapsTheLoopBecauseALaneMayLeave()
    {
        var steps = Run("UniformWhileWithReturn");

        Assert.Equal(["enter 0", "while(", "if(", "return", ")", ")", "arrive 0"], TokensOf(steps.Reconverged));
        Assert.Equal(1, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_ReturnInNestedRegionsOfADeviceFunction_LeavesTheInnermostFirst()
    {
        var steps = Run("LeaveBothRegions", isDeviceFunction: true);

        string[] expected =
        [
            "enter 0", "if(", "enter 1", "while(", "if(", "leave 1", "leave 0", "return", ")", ")", "arrive 1", ")",
            "arrive 0"
        ];
        Assert.Equal(expected, TokensOf(steps.Reconverged));
        Assert.Equal(2, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Insert_SuspendingCall_WrapsTheCallStatement()
    {
        var steps = Run("SuspendingCallRegion");

        Assert.Equal(["enter 0", "arrive 0"], TokensOf(steps.Reconverged));
        Assert.Equal(1, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Insert_TwoIfs_NumbersTheRegionsInOrder()
    {
        var steps = Run("TwoIfs");

        Assert.Equal(["enter 0", "if(", ")", "arrive 0", "enter 1", "if(", ")", "arrive 1"],
            TokensOf(steps.Reconverged));
        Assert.Equal(2, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_AndOperandWithSplit_WrapsTheIfOfTheOperand()
    {
        var steps = Run("AndOperandRegion");

        Assert.Equal(["enter 0", "if(", ")", "arrive 0"], TokensOf(steps.Reconverged));
        Assert.Equal(1, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_TernaryWithUniformCondition_AddsNoRegion()
    {
        var steps = Run("TernaryUniform");

        Assert.Equal(["if(", "else", ")"], TokensOf(steps.Reconverged));
        Assert.Equal(0, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_TernaryWithThreadVariantCondition_WrapsTheIf()
    {
        var steps = Run("TernaryVariant");

        Assert.Equal(["enter 0", "if(", "else", ")", "arrive 0"], TokensOf(steps.Reconverged));
        Assert.Equal(1, steps.Reconverged.RegionCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_EmbeddedIf_PutsTheRegionInABlock()
    {
        var steps = Run("EmbeddedRegion");

        Assert.Equal(["if(", "enter 0", "if(", ")", "arrive 0", ")"], TokensOf(steps.Reconverged));
        var outerIf = steps.Reconverged.Body.Body.Statements.OfType<IrIf>().Single();
        var thenBlock = Assert.IsType<IrBlock>(outerIf.Then);
        Assert.Equal(3, thenBlock.Statements.Length);
        Assert.IsType<IrIf>(thenBlock.Statements[1]);
        Assert.True(thenBlock.Source.Synthesized);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_CommentBeforeIf_MovesTheCommentToTheEnter()
    {
        var steps = Run("CommentedIf");
        var statements = steps.Reconverged.Body.Body.Statements;

        var enterIndex = FindRegionStatementIndex(statements, IrRuntimeFunction.WarpRegionEnter);
        Assert.True(enterIndex >= 0);
        Assert.Equal<string>(["// note"], statements[enterIndex].CommentsBefore);
        var innerIf = Assert.IsType<IrIf>(statements[enterIndex + 1]);
        Assert.Empty(innerIf.CommentsBefore);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_CommentBeforeWhileWithConditionSplit_KeepsTheLoweredCondition()
    {
        var steps = Run("CommentedWhile");
        var statements = steps.Reconverged.Body.Body.Statements;

        Assert.Equal(["enter 0", "while(", ")", "arrive 0"], TokensOf(steps.Reconverged));
        var enterIndex = FindRegionStatementIndex(statements, IrRuntimeFunction.WarpRegionEnter);
        Assert.True(enterIndex >= 0);
        Assert.Equal<string>(["// loop"], statements[enterIndex].CommentsBefore);
        var innerWhile = Assert.IsType<IrWhile>(statements[enterIndex + 1]);
        Assert.Empty(innerWhile.CommentsBefore);
        Assert.Empty(IrReconvergedInvariants.Verify(steps.Reconverged, steps.Compilation,
            isDeviceFunction: false, reachesActiveMask: true));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_MethodThatDoesNotReachActiveMask_AddsNoRegion()
    {
        var steps = Run("NotReachingActiveMask");

        Assert.Equal(["if(", ")"], TokensOf(steps.Reconverged));
        Assert.Equal(0, steps.Reconverged.RegionCount);
        var ifs = IrNodeChildren.DescendantsAndSelf(steps.Reconverged.Body.Body).OfType<IrIf>().ToArray();
        Assert.NotEmpty(ifs);
        Assert.All(ifs, static @if => Assert.Equal(IrUniformity.NotRequired, @if.Uniformity));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Linearize_ThreadVariantIf_KeepsTheRegionStatementsAroundTheFlattenedIf()
    {
        var steps = Run("ThreadVariantIf");
        string[] expected =
        [
            "decl",
            "enter 0",
            "if!goto __gsharp_BranchEnd_0",
            "split",
            "expr",
            "__gsharp_BranchEnd_0:",
            "arrive 0",
            "split",
            "expr"
        ];

        Assert.Equal(expected, steps.Linear.Select(s => Shape(s, steps.Compilation)).ToArray());
        Assert.Empty(IrInvariants.VerifyLinear(steps.Linear, steps.Compilation));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Insert_EveryCase_HasNoViolation()
    {
        var compilation = IrTestCases.Compile(IrLoweringSteps.Cases);
        var checkedCount = 0;

        foreach (var built in IrTestCases.BuildAll(IrLoweringSteps.Cases, IrLoweringSteps.CasesType))
        {
            var name = built.Symbol.Name;
            var isDevice = name is "IfAsLastStatementDevice" or "LeaveBothRegions" or "Step";
            var steps = IrLoweringSteps.Run(IrLoweringSteps.Cases, IrLoweringSteps.CasesType, name, isDevice);
            var reaches = SuspensionAnalysis.ReachesActiveMask(built.Symbol, compilation);

            Assert.Empty(IrReconvergedInvariants.Verify(steps.Reconverged, steps.Compilation, isDevice, reaches));
            checkedCount++;
        }

        Assert.Equal(20, checkedCount);
    }

    private static IrLoweringSteps Run(string method, bool isDeviceFunction = false) =>
        IrLoweringSteps.Run(IrLoweringSteps.Cases, IrLoweringSteps.CasesType, method, isDeviceFunction);

    private static int FindRegionStatementIndex(ImmutableArray<IrStatement> statements, IrRuntimeFunction function)
    {
        for (var index = 0; index < statements.Length; index++)
        {
            if (statements[index] is IrExpressionStatement { Expression: IrRuntimeCall call } &&
                call.Function == function)
            {
                return index;
            }
        }

        return -1;
    }

    private static string[] TokensOf(IrReconvergedBody reconverged)
    {
        var tokens = new List<string>();
        foreach (var statement in reconverged.Body.Body.Statements)
        {
            CollectTokens(statement, tokens);
        }

        return [.. tokens];
    }

    private static void CollectTokens(IrStatement statement, List<string> tokens)
    {
        switch (statement)
        {
            case IrExpressionStatement { Expression: IrRuntimeCall call }:
                var prefix = call.Function switch
                {
                    IrRuntimeFunction.WarpRegionEnter => "enter",
                    IrRuntimeFunction.WarpRegionLeave => "leave",
                    IrRuntimeFunction.WarpReconvergeArrive => "arrive",
                    _ => throw new InvalidOperationException($"Unexpected runtime function: {call.Function}")
                };
                tokens.Add($"{prefix} {call.Site}");
                break;
            case IrReturn:
                tokens.Add("return");
                break;
            case IrIf @if:
                tokens.Add("if(");
                CollectTokens(@if.Then, tokens);
                if (@if.Else is { } elseBranch)
                {
                    tokens.Add("else");
                    CollectTokens(elseBranch, tokens);
                }

                tokens.Add(")");
                break;
            case IrWhile @while:
                tokens.Add("while(");
                CollectTokens(@while.Body, tokens);
                tokens.Add(")");
                break;
            case IrBlock block:
                foreach (var inner in block.Statements)
                {
                    CollectTokens(inner, tokens);
                }

                break;
        }
    }

    private static string Shape(IrStatement statement, Compilation compilation) => statement switch
    {
        IrExpressionStatement { Expression: IrRuntimeCall { Function: IrRuntimeFunction.WarpRegionEnter } call } =>
            $"enter {call.Site}",
        IrExpressionStatement { Expression: IrRuntimeCall { Function: IrRuntimeFunction.WarpRegionLeave } call } =>
            $"leave {call.Site}",
        IrExpressionStatement { Expression: IrRuntimeCall { Function: IrRuntimeFunction.WarpReconvergeArrive } call } =>
            $"arrive {call.Site}",
        IrLabel label => $"{label.Label.Name}:",
        IrGoto @goto => $"goto {@goto.Target.Name}",
        IrConditionalGoto { JumpIfTrue: false } cond => $"if!goto {cond.Target.Name}",
        IrExpressionStatement { Expression: IrCall call } when IrSplitLowering.HoldsSplit(call, compilation) =>
            "split",
        IrLocalDeclaration { Initializer: IrCall call } when IrSplitLowering.HoldsSplit(call, compilation) =>
            "split",
        IrLocalDeclaration => "decl",
        IrExpressionStatement => "expr",
        IrIf => "if",
        IrWhile => "while",
        IrBlock => "block",
        IrReturn => "return",
        _ => throw new InvalidOperationException($"Unexpected statement shape: {statement.GetType().Name}")
    };
}
