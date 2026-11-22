#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests for <see cref="ReconvergenceInserter"/> (#844): the lanes of a warp that took different ways through a
/// thread-dependent branch or loop of a method that reaches <c>Warp.ActiveMask()</c> wait for each other where the
/// branch ends. That place must be the immediate post-dominator of the branch in the control flow graph that
/// <see cref="UniformityAnalysis"/> builds, which is where the GPU runs them together again.
/// </summary>
[Category(TestCategory.Compiler)]
public class ReconvergenceInserterTests : StateMachineTestBase
{
    private const string TestCase = "ReconvergenceCases.cs";

    // The user calls (Mark, Next) that are the first call after each reconvergence point of the lowered body, and the
    // ones that UniformityAnalysis finds at the immediate post-dominator of each thread-variant branch that holds a split
    // point. A branch that a lane leaves by a jump (continue) also reconverges where it ends for the lanes that did not
    // jump, before a call to Warp.ActiveMask() here, which costs a round and changes no group
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("IfKernel", "Mark(1)")]
    [InlineData("IfElseKernel", "Mark(2)")]
    [InlineData("DivergentForKernel", "Mark(3)")]
    [InlineData("BreakKernel", "Mark(4)")]
    [InlineData("ContinueKernel", "Next(k)")]
    [InlineData("NestedKernel", "Mark(5)", "Mark(6)")]
    [InlineData("UniformSwitchBreakKernel", "Mark(9)")]
    [InlineData("ForwardGotoKernel", "Mark(11)")]
    [InlineData("GotoOutOfUniformIfKernel", "Mark(12)")]
    [InlineData("ForwardGotoOverNestedBlockKernel", "Mark(16)")]
    public void Insert_ThreadDependentBranch_ReconvergesAtItsImmediatePostDominator(string kernel,
        params string[] expected)
    {
        var (lowered, joins) = LowerAndAnalyze(kernel);

        Assert.Equal(expected, lowered);
        Assert.Equal(expected, joins);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Insert_CrossingGotos_ReconvergesAtBothLabels()
    {
        var (lowered, joins) = LowerAndAnalyze("CrossingGotosKernel");
        Assert.Equal(new[] { "Mark(13)", "Mark(14)" }, lowered);
        Assert.Equal(new[] { "Mark(14)" }, joins);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Insert_BackwardGotoLoop_ReconvergesAfterTheLoop()
    {
        var (lowered, _) = LowerAndAnalyze("BackwardGotoKernel");
        Assert.Equal(new[] { "Mark(17)" }, lowered);
    }

    // A goto loop whose last statement is a block closes inside it, right after the statement with the last goto back
    // to its label (also when nested inside an inner loop's block, where top closes before the publish after its goto
    // and has no user call after its pass); one whose last statement is an if closes inside the if right after the
    // goto, and the region of the if ends right before Mark(32)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("GotoLoopInBlockKernel", "Mark(28)")]
    [InlineData("GotoLoopInBlockAroundInnerLoopKernel", "Mark(30)")]
    [InlineData("GotoLoopInIfKernel", "Mark(32)")]
    public void Insert_GotoLoopEndingInBlock_ClosesAfterTheLastGotoInTheBlock(string kernel, params string[] expected)
    {
        var (lowered, _) = LowerAndAnalyze(kernel);
        Assert.Equal(expected, lowered);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_GotoLoopEndingInBlock_EmitsArriveInsideTheBlock() =>
        VerifyPipelineLowerFromTestCase("ReconvergenceGotoLoopInBlock.cs");

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_GotoLoopEndingInIf_EmitsGateInBothBranches() =>
        VerifyPipelineLowerFromTestCase("ReconvergenceGotoLoopInIf.cs");

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_GotoLoopWithTwoBackwardGotos_EmitsLatchAtEachGoto() =>
        VerifyPipelineLowerFromTestCase("ReconvergenceGotoLoopLatch.cs");

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_GotoLoopThroughForLoop_EmitsLatchAtBackwardGoto() =>
        VerifyPipelineLowerFromTestCase(
            "ReconvergenceGotoLoopThroughLoop.cs",
            kernelName: "GotoLoopThroughForLoopKernel");

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_GotoLoopLeftInsideForLoop_EmitsBreakGateAndJoinGate() =>
        VerifyPipelineLowerFromTestCase(
            "ReconvergenceGotoLoopThroughLoop.cs",
            kernelName: "GotoLoopLeftInsideForLoopKernel");

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_GotoLoopEndingInSwitchSection_EmitsBreakGateAndJoinLabel() =>
        VerifyPipelineLowerFromTestCase("ReconvergenceGotoLoopInSwitch.cs");

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopWithSwitchVarPatternExit_ClosesBeforeGate()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", TestCase));
        var method = FindMethod(tree, "GotoLoopSwitchVarPatternBeforeLastKernel");
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method)
                     ?? throw new InvalidOperationException("GotoLoopSwitchVarPatternBeforeLastKernel has no symbol.");

        var lowered = AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction: false);
        var arriveCount = lowered.LoweredBody
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive));
        var enterCount = lowered.LoweredBody
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(static call => Calls(call, GSharpInternalNames.Prelude.WarpRegionEnter));

        Assert.Equal(6, arriveCount);
        Assert.Equal(5, enterCount);
        var (afterReconvergence, _) = LowerAndAnalyze("GotoLoopSwitchVarPatternBeforeLastKernel");
        Assert.Equal(new[] { "Mark(34)", "Mark(35)" }, afterReconvergence);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopWithCheckedDeadLabelExit_ClosesBeforeGate()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", TestCase));
        var method = FindMethod(tree, "GotoLoopCheckedDeadLabelBeforeLastKernel");
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method)
                     ?? throw new InvalidOperationException("GotoLoopCheckedDeadLabelBeforeLastKernel has no symbol.");

        var lowered = AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction: false);
        var arriveCount = lowered.LoweredBody
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive));
        var enterCount = lowered.LoweredBody
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(static call => Calls(call, GSharpInternalNames.Prelude.WarpRegionEnter));

        Assert.Equal(6, arriveCount);
        Assert.Equal(5, enterCount);
        var (afterReconvergence, _) = LowerAndAnalyze("GotoLoopCheckedDeadLabelBeforeLastKernel");
        Assert.Equal(new[] { "Mark(37)", "Mark(38)" }, afterReconvergence);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopWithGateInInnerForAndContinueInOuterFor_ClosesAtOuterExit()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", TestCase));
        var method = FindMethod(tree, "GotoLoopInNestedForWithOuterContinueKernel");
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method)
                     ?? throw new InvalidOperationException("GotoLoopInNestedForWithOuterContinueKernel has no symbol.");

        var lowered = AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction: false);
        var stackLowered = AstLoweringPipeline.LowerForStack(method, model, symbol, isDeviceFunction: false);

        Assert.NotEmpty(lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive)));
        Assert.NotEmpty(stackLowered.Body.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.StackWarp.Reconverge)));

        var afterReconvergence = lowered.LoweredBody
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergePass))
            .Select(static pass => FirstUserCallAfter(pass))
            .OfType<string>()
            .ToArray();

        Assert.Contains("Mark(40)", afterReconvergence);
        Assert.Contains("Mark(41)", afterReconvergence);

        var mark39 = stackLowered.Body
            .DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Single(static s => s.Expression is InvocationExpressionSyntax inv && Describe(inv) == "Mark(39)");
        var mark39Block = Assert.IsType<BlockSyntax>(mark39.Parent);
        var mark39Index = mark39Block.Statements.IndexOf(mark39);
        Assert.Contains(
            mark39Block.Statements.Take(mark39Index).Select(static s => (s as ExpressionStatementSyntax)?.Expression).OfType<InvocationExpressionSyntax>(),
            static call => Calls(call, GSharpInternalNames.StackWarp.Reconverge));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopWithContinueBeforeGateInFor_EmitsGateBeforeBreakBlock()
    {
        var (lowered, stackLowered) = LowerFromTestCase("GotoLoopWithContinueBeforeGateInForKernel", isDeviceFunction: false);

        Assert.NotEmpty(lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive)));
        var afterReconvergence = GetCallsAfterReconvergencePass(lowered);
        Assert.Contains("Mark(43)", afterReconvergence);

        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(42)");
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(43)");
        Assert.Contains(
            stackLowered.Body.DescendantNodes().OfType<GotoStatementSyntax>(),
            static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: var label } &&
                        label.StartsWith(GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix, StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopWithLabeledBreakIntoLoopInNestedFor_GatesInnerForOnly()
    {
        var (lowered, stackLowered) = LowerFromTestCase("GotoLoopWithLabeledBreakIntoLoopInNestedForKernel", isDeviceFunction: false);

        Assert.NotEmpty(lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive)));
        var afterReconvergence = GetCallsAfterReconvergencePass(lowered);
        Assert.Contains("Mark(45)", afterReconvergence);
        Assert.Contains("Mark(46)", afterReconvergence);

        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(44)");
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(46)");
        Assert.DoesNotContain(
            stackLowered.Body.DescendantNodes().OfType<GotoStatementSyntax>(),
            static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: var label } &&
                        label.StartsWith(GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix, StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_NestedGotoLoopsWithContinueAndBreakExitingInnerLoop_ClosesOuterAtGateAndInnerAtBreak()
    {
        var (lowered, stackLowered) = LowerFromTestCase("NestedGotoLoopsWithContinueAndBreakExitingInnerLoopKernel", isDeviceFunction: false);

        Assert.NotEmpty(lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive)));

        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(47)");
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(49)");

        var mark47 = FindMarkStatement(stackLowered.Body, "Mark(47)");
        var mark47Block = Assert.IsType<BlockSyntax>(mark47.Parent);
        var mark47Index = mark47Block.Statements.IndexOf(mark47);
        var mark47LeavesBeforeReconverge = mark47Block.Statements
            .Take(mark47Index)
            .Reverse()
            .SkipWhile(static s => s is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax c } &&
                                   (Calls(c, GSharpInternalNames.StackWarp.RegionEnter) ||
                                    Calls(c, GSharpInternalNames.StackWarp.Reconverge)))
            .TakeWhile(static s => s is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax c } &&
                                   Calls(c, GSharpInternalNames.StackWarp.RegionLeave))
            .Count();
        // Leaves C, F, B_L1, G_L1, and B_L0 when closing outer L0 at the gate.
        Assert.Equal(5, mark47LeavesBeforeReconverge);

        var joinGoto = stackLowered.Body
            .DescendantNodes()
            .OfType<GotoStatementSyntax>()
            .Single(static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: var label } &&
                                label.StartsWith(GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix, StringComparison.Ordinal));
        var joinGotoBlock = Assert.IsType<BlockSyntax>(joinGoto.Parent);
        var joinGotoIndex = joinGotoBlock.Statements.IndexOf(joinGoto);
        Assert.Contains(
            joinGotoBlock.Statements
                .Take(joinGotoIndex)
                .Select(static s => (s as ExpressionStatementSyntax)?.Expression)
                .OfType<InvocationExpressionSyntax>(),
            static call => Calls(call, GSharpInternalNames.StackWarp.Reconverge));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopWithConditionalForwardGotoToMemberBeforeGate_EmitsGateBeforeReturn()
    {
        var (lowered, stackLowered) = LowerFromTestCase("GotoLoopWithConditionalForwardGotoToMemberBeforeGateKernel", isDeviceFunction: false);

        Assert.NotEmpty(lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive)));

        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(50)");
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(51)");
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_DeviceFunctionWithGotoLoopGatedInForAndEarlyReturn_LeavesStructuralRegionsOnReturn()
    {
        var (lowered, stackLowered) = LowerFromTestCase("GotoInForWithEarlyReturn", isDeviceFunction: true);

        Assert.NotEmpty(lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive)));

        var mark52 = FindMarkStatement(stackLowered.Body, "Mark(52)");
        var mark52Block = Assert.IsType<BlockSyntax>(mark52.Parent);
        var mark52Index = mark52Block.Statements.IndexOf(mark52);
        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(52)");

        var earlyReturn = mark52Block.Statements.OfType<ReturnStatementSyntax>().Single();
        var earlyReturnIndex = mark52Block.Statements.IndexOf(earlyReturn);
        var regionLeaveCount = mark52Block.Statements
            .Skip(mark52Index + 1)
            .Take(earlyReturnIndex - (mark52Index + 1))
            .Select(static s => (s as ExpressionStatementSyntax)?.Expression)
            .OfType<InvocationExpressionSyntax>()
            .Count(static call => Calls(call, GSharpInternalNames.StackWarp.RegionLeave));
        Assert.Equal(2, regionLeaveCount);

        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(53)");
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopTopLevelForwardGotoToMemberPastReturnGate_EmitsLatchAndGate()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "GotoLoopTopLevelForwardGotoToMemberPastReturnGateKernel",
            isDeviceFunction: false);

        Assert.NotEmpty(lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive)));
        Assert.Contains("Mark(55)", GetCallsAfterReconvergencePass(lowered));

        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(54)");
        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(55)");
        AssertHasLatchBeforeAllBackGotos(stackLowered.Body, "L1", 2);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopInForForwardGotoToMemberPastBreakGate_EmitsLatchAndGate()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "GotoLoopInForForwardGotoToMemberPastBreakGateKernel",
            isDeviceFunction: false);

        Assert.NotEmpty(lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive)));
        Assert.Contains("Mark(58)", GetCallsAfterReconvergencePass(lowered));

        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(56)");
        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(57)");
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(58)");
        AssertHasLatchBeforeAllBackGotos(stackLowered.Body, "L1", 2);
        var joinGotoCount = stackLowered.Body
            .DescendantNodes()
            .OfType<GotoStatementSyntax>()
            .Count(static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: var label } &&
                               label.StartsWith(GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix, StringComparison.Ordinal));
        Assert.Equal(3, joinGotoCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopPlainBlockForwardGotoToMemberPastReturnGate_EmitsLatchAndGate()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "GotoLoopPlainBlockForwardGotoToMemberPastReturnGateKernel",
            isDeviceFunction: false);

        Assert.NotEmpty(lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive)));
        Assert.Contains("Mark(60)", GetCallsAfterReconvergencePass(lowered));

        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(59)");
        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(60)");
        AssertHasLatchBeforeAllBackGotos(stackLowered.Body, "L1", 2);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopInIfForwardGotoToMemberPastReturnGate_EmitsLatchAndGate()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "GotoLoopInIfForwardGotoToMemberPastReturnGateKernel",
            isDeviceFunction: false);

        Assert.NotEmpty(lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive)));
        Assert.Contains("Mark(63)", GetCallsAfterReconvergencePass(lowered));

        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(61)");
        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(62)");
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(63)");
        AssertHasLatchBeforeAllBackGotos(stackLowered.Body, "L1", 2);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopCheckedBlockForwardGotoToMemberPastReturnGate_EmitsLatchAndGate()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "CheckedBlockForwardGotoToMemberPastReturnGateKernel",
            isDeviceFunction: false);
        AssertHasBalancedStackRegionsAndTrailingGotoLatch(
            stackLowered,
            lowered,
            finalMark: "Mark(65)",
            gateMark: "Mark(64)");
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopGateWithForwardGotoToMemberPastReturn_PreservesBothLabelStacks()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "GateWithForwardGotoToMemberPastReturnKernel",
            isDeviceFunction: false);
        AssertHasBalancedRegionSites(stackLowered, lowered);
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(90)");
        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(91)");
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(92)");
        AssertHasLatchBeforeAllBackGotos(stackLowered.Body, "L1", 2);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_LoopNotDroppingCycleWithContinueAndUnreachableEnd_RestoresOpenAndBalancesBodyRegion()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "LoopWithContinueAndUnreachableEndKernel",
            isDeviceFunction: false);
        AssertHasBalancedStackRegionsAndTrailingGotoLatch(
            stackLowered,
            lowered,
            finalMark: "Mark(68)",
            afterLoopMark: "Mark(67)",
            expectLoopBodyContinueRegion: true);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_LoopNotDroppingCycleWithoutContinueAndUnreachableEnd_PreservesOpenAfterLoop()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "LoopWithoutContinueAndUnreachableEndKernel",
            isDeviceFunction: false);
        AssertHasBalancedStackRegionsAndTrailingGotoLatch(
            stackLowered,
            lowered,
            finalMark: "Mark(71)",
            afterLoopMark: "Mark(70)");
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_DoWhileFalseWithInnerForEarlyReturnAndTrailingGoto_ClosesAfterDoWhile()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "DoWhileFalseWithInnerForEarlyReturnKernel",
            isDeviceFunction: false);
        AssertHasBalancedStackRegionsAndTrailingGotoLatch(
            stackLowered,
            lowered,
            finalMark: "Mark(73)");
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopInSwitchWithoutDefault_EmitsFallthroughGateAndJoinLabel()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "GotoLoopInSwitchWithoutDefaultKernel",
            isDeviceFunction: false);
        AssertHasBalancedRegionSites(stackLowered, lowered);
        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(74)");
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(75)");
        Assert.Contains(
            stackLowered.Body.DescendantNodes().OfType<GotoStatementSyntax>(),
            static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: var label } &&
                        label.StartsWith(GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix, StringComparison.Ordinal));
        var (afterReconvergence, _) = LowerAndAnalyze("GotoLoopInSwitchWithoutDefaultKernel");
        Assert.Contains("Mark(75)", afterReconvergence);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopInSwitchWithMultipleSectionsAndBreak_EmitsBreakGatesAndJoinLabel()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "GotoLoopInSwitchWithMultipleSectionsAndBreakKernel",
            isDeviceFunction: false);
        AssertHasBalancedRegionSites(stackLowered, lowered);
        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(76)");
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(78)");
        Assert.Contains(
            stackLowered.Body.DescendantNodes().OfType<GotoStatementSyntax>(),
            static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: var label } &&
                        label.StartsWith(GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix, StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopInSwitchSectionWithIfReconcile_EmitsGateAndReconcilesBranches()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "GotoLoopInSwitchSectionWithIfReconcileKernel",
            isDeviceFunction: false);
        AssertHasBalancedRegionSites(stackLowered, lowered);
        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(79)");
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(80)");
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(81)");
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopInUniformSwitchSection_OpensSwitchRegionAndEmitsGate()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "GotoLoopInUniformSwitchSectionKernel",
            isDeviceFunction: false);
        AssertHasBalancedRegionSites(stackLowered, lowered);
        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(82)");
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(83)");
        Assert.Contains(
            stackLowered.Body.DescendantNodes().OfType<GotoStatementSyntax>(),
            static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: var label } &&
                        label.StartsWith(GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix, StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopInWhenPatternSwitch_EmitsFallthroughGateAfterSwitch()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "GotoLoopInWhenPatternSwitchKernel",
            isDeviceFunction: false);
        AssertHasBalancedRegionSites(stackLowered, lowered);
        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(84)");

        var statements = stackLowered.Body.Statements;
        var switchIndex = statements
            .Select(static (statement, index) => (statement, index))
            .Single(static pair => pair.statement is SwitchStatementSyntax)
            .index;
        var joinIndex = statements
            .Select(static (statement, index) => (statement, index))
            .Single(static pair =>
                pair.statement is LabeledStatementSyntax labeled &&
                labeled.Identifier.ValueText.StartsWith(
                    GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix,
                    StringComparison.Ordinal))
            .index;
        Assert.True(switchIndex < joinIndex,
            "Expected switch statement before its reconverge join label.");

        var fallthroughReconvergeSites = statements
            .Skip(switchIndex + 1)
            .Take(joinIndex - switchIndex - 1)
            .Select(static s => (s as ExpressionStatementSyntax)?.Expression)
            .OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.StackWarp.Reconverge))
            .Select(static call => ExtractRegionSite(call, siteIndex: 1))
            .ToArray();
        var enterSites = ExtractRegionSites(
            stackLowered.Body, GSharpInternalNames.StackWarp.RegionEnter, siteIndex: 1);
        Assert.Single(fallthroughReconvergeSites);
        Assert.Equal(enterSites[0], fallthroughReconvergeSites[0]);

        var mark85Statement = FindMarkStatement(stackLowered.Body, "Mark(85)");
        var mark85Index = statements.IndexOf(mark85Statement);
        var postJoinReconvergeSites = statements
            .Skip(joinIndex + 1)
            .Take(mark85Index - joinIndex - 1)
            .Select(static s => (s as ExpressionStatementSyntax)?.Expression)
            .OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.StackWarp.Reconverge))
            .Select(static call => ExtractRegionSite(call, siteIndex: 1))
            .ToArray();
        Assert.Single(postJoinReconvergeSites);
        Assert.NotEqual(fallthroughReconvergeSites[0], postJoinReconvergeSites[0]);
        Assert.Contains(
            stackLowered.Body.DescendantNodes().OfType<GotoStatementSyntax>(),
            static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: var label } &&
                        label.StartsWith(GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix, StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopInSwitchSectionWithSelfGotoCase_EmitsLatchAndGate()
    {
        var (lowered, stackLowered) = LowerFromTestCase(
            "GotoLoopInSwitchSectionWithSelfGotoCaseKernel",
            isDeviceFunction: false);
        AssertHasLatchBeforeAllBackGotos(stackLowered.Body, "L1", 1);
        AssertHasBalancedRegionSites(stackLowered, lowered);
        AssertHasGateStackReconvergeBefore(stackLowered.Body, "Mark(86)");
        AssertHasPrecedingStackReconverge(stackLowered.Body, "Mark(87)");
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("GotoCaseJoinKernel", "Mark(20)", "Mark(21)")]
    [InlineData("GotoLabelJoinKernel", "Mark(22)", "Mark(23)")]
    public void Insert_ThreadVariantSwitchCrossSectionJump_ReconvergesAtJoinAndSwitchEnd(string kernel,
        params string[] expected)
    {
        var (lowered, _) = LowerAndAnalyze(kernel);
        Assert.Equal(expected, lowered);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ThreadVariantSwitchNoCrossJumpKernel")]
    [InlineData("UniformSwitchGotoCaseNoJoinKernel")]
    [InlineData("WhenSwitchNoJoinKernel")]
    public void Insert_SwitchWithoutSecondEntryJoin_ReconvergesOnlyAtSwitchEnd(string kernel)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", TestCase));
        var method = FindMethod(tree, kernel);
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method)
                     ?? throw new InvalidOperationException($"{kernel} has no symbol.");

        var lowered = AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction: false);

        // The join labels become states of the state machine, so count the reconvergence points instead: the switch
        // region is the only one; a join would add its own
        Assert.Single(
            lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_BackwardGotoLoop_EmitsRegionAroundLoop() =>
        VerifyPipelineLowerFromTestCase("ReconvergenceGotoLoop.cs");

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_SwitchSecondEntryJoin_EmitsSwitchJoinRegion() =>
        VerifyPipelineLowerFromTestCase("ReconvergenceSwitchJoin.cs");

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_SwitchSecondEntryChain_EmitsOrderedSwitchJoins() =>
        VerifyPipelineLowerFromTestCase("ReconvergenceSwitchJoinChain.cs");

    // The label of a forward goto in a thread-dependent switch section is also where lanes of another section join, and
    // the lanes of the forward goto hold the join of an earlier section that another goto of theirs reaches: the goto
    // goes to the join of its range and keeps every region below the range, that switch join included
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_ForwardGotoToSwitchJoinLabel_LeavesNoRegionBelowItsRange()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", TestCase));
        var method = FindMethod(tree, "ForwardGotoToSwitchJoinLabelKernel");
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method)
                     ?? throw new InvalidOperationException("ForwardGotoToSwitchJoinLabelKernel has no symbol.");

        var lowered = AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction: false);

        // if ((t & 4) != 0) goto shared;
        var forwardGoto = lowered.LoweredBody
            .DescendantNodes()
            .OfType<IfStatementSyntax>()
            .Single(static branch => branch.Condition is BinaryExpressionSyntax
            {
                Left: ParenthesizedExpressionSyntax
                {
                    Expression: BinaryExpressionSyntax { Right: LiteralExpressionSyntax { Token.ValueText: "4" } }
                }
            });
        Assert.DoesNotContain(
            forwardGoto.Statement.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            static call => Calls(call, GSharpInternalNames.Prelude.WarpRegionLeave));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Insert_GotoLoopAtKernelEnd_EmitsRegionAroundTrailingBranch()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", TestCase));
        var method = FindMethod(tree, "GotoLoopAtKernelEndKernel");
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method)
                     ?? throw new InvalidOperationException("GotoLoopAtKernelEndKernel has no symbol.");

        var lowered = AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction: false);

        var arriveCount = lowered.LoweredBody
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive));
        Assert.Equal(2, arriveCount);
        Assert.Equal(4, lowered.WarpSiteCount);
    }

    // A uniform branch, a branch without a split point, and a branch that ends the kernel get no reconvergence point
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("UniformAndPlainKernel")]
    [InlineData("LastStatementKernel")]
    [InlineData("UniformSwitchDirectBreakKernel")]
    [InlineData("ForwardGotoNoSplitKernel")]
    [InlineData("BackwardGotoNoSplitKernel")]
    public void Insert_UniformBranchOrKernelEnd_AddsNoReconvergencePoint(string kernel)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", TestCase));
        var method = FindMethod(tree, kernel);
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method)
                     ?? throw new InvalidOperationException($"{kernel} has no symbol.");

        var lowered = AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction: false);

        Assert.DoesNotContain(
            lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive));
        Assert.Equal(0, lowered.WarpSiteCount - CountWarpCalls(lowered.LoweredBody));
    }

    private static (string[] Lowered, string[] Joins) LowerAndAnalyze(string kernel)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", TestCase));
        var method = FindMethod(tree, kernel);
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method)
                     ?? throw new InvalidOperationException($"{kernel} has no symbol.");

        var lowered = AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction: false);
        var afterReconvergence = lowered.LoweredBody
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergePass))
            .Select(static pass => FirstUserCallAfter(pass))
            .OfType<string>()
            .OrderBy(static call => call, StringComparer.Ordinal)
            .ToArray();

        Assert.True(new MethodFlowGraph(model, method).TryGetUniformity(out var uniformity));
        var joins = uniformity.GetDivergentBranches()
            .Where(static branch => branch.HoldsSplitPoint)
            .SelectMany(static branch => branch.JoinCalls)
            .OfType<InvocationExpressionSyntax>()
            .Select(Describe)
            .OfType<string>()
            .Distinct()
            .OrderBy(static call => call, StringComparer.Ordinal)
            .ToArray();
        return (afterReconvergence, joins);
    }

    private static MethodDeclarationSyntax FindMethod(SyntaxTree tree, string name) =>
        tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == name);

    // Mark(<id>) or Next(k) for a user call, whatever the lowering made of the local k; null for any other call
    private static string? Describe(InvocationExpressionSyntax call) =>
        call switch
        {
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "Mark" },
                ArgumentList.Arguments: [{ Expression: LiteralExpressionSyntax { Token.ValueText: var id } }]
            } => $"Mark({id})",
            { Expression: IdentifierNameSyntax { Identifier.ValueText: "Next" } } => "Next(k)",
            _ => null
        };

    private static bool Calls(InvocationExpressionSyntax call, string name) =>
        call.Expression is IdentifierNameSyntax { Identifier.ValueText: var callee } && callee == name;

    // The first call after the pass of a reconvergence point, when it is a user call. The states of the lowered body
    // follow each other in the order of the source, so the next call in the text is the next one that runs (a state
    // that only jumps to the next state has none)
    private static string? FirstUserCallAfter(InvocationExpressionSyntax pass)
    {
        var body = pass.Ancestors().OfType<BlockSyntax>().Last();
        var call = body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(c => c.SpanStart > pass.Span.End);
        return call != null ? Describe(call) : null;
    }

    // The warp collectives of a lowered body: one warp site each, besides the reserved sites of the regions
    private static int CountWarpCalls(BlockSyntax body) =>
        body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(static call => Calls(call, GSharpInternalNames.Prelude.WarpPublish));

    private static void AssertHasBalancedStackRegionsAndTrailingGotoLatch(
        StackLoweredBody stackLowered,
        LoweredStateMachine lowered,
        string finalMark,
        string? gateMark = null,
        string? afterLoopMark = null,
        bool expectLoopBodyContinueRegion = false)
    {
        AssertHasLatchBeforeAllBackGotos(stackLowered.Body, "L1", 2);
        AssertHasBalancedRegionSites(stackLowered, lowered);

        if (gateMark != null)
        {
            AssertHasGateStackReconvergeBefore(stackLowered.Body, gateMark);
        }

        if (afterLoopMark != null)
        {
            AssertHasPrecedingStackReconverge(stackLowered.Body, afterLoopMark);
        }

        AssertHasGateStackReconvergeBefore(stackLowered.Body, finalMark);

        if (expectLoopBodyContinueRegion)
        {
            var continueLabel = stackLowered.Body
                .DescendantNodes()
                .OfType<LabeledStatementSyntax>()
                .Single(static labeled => labeled.Identifier.ValueText.StartsWith(
                    GSharpInternalNames.Lowering.ReconvergeContinueLabelPrefix,
                    StringComparison.Ordinal));
            var block = Assert.IsType<BlockSyntax>(continueLabel.Parent);
            var index = block.Statements.IndexOf(continueLabel);
            Assert.True(index + 1 < block.Statements.Count,
                "Expected loop-body reconvergence call after continue label.");
            var nextInvocation = Assert.IsType<ExpressionStatementSyntax>(block.Statements[index + 1])
                .Expression as InvocationExpressionSyntax;
            Assert.NotNull(nextInvocation);
            Assert.True(Calls(nextInvocation, GSharpInternalNames.StackWarp.Reconverge),
                $"Expected {GSharpInternalNames.StackWarp.Reconverge} after {continueLabel.Identifier.ValueText}, got {nextInvocation}.");
            var continueSite = ExtractRegionSite(nextInvocation, siteIndex: 1);
            var enterSites = ExtractRegionSites(stackLowered.Body, GSharpInternalNames.StackWarp.RegionEnter, siteIndex: 1);
            Assert.Contains(continueSite, enterSites);
        }
    }

    private static void AssertHasBalancedRegionSites(
        StackLoweredBody stackLowered,
        LoweredStateMachine lowered)
    {
        var stackEnters = ExtractRegionSites(stackLowered.Body, GSharpInternalNames.StackWarp.RegionEnter, siteIndex: 1);
        var stackLeaves = ExtractRegionSites(stackLowered.Body, GSharpInternalNames.StackWarp.RegionLeave, siteIndex: 1);
        var stackReconverges = ExtractRegionSites(stackLowered.Body, GSharpInternalNames.StackWarp.Reconverge, siteIndex: 1);

        Assert.NotEmpty(stackEnters);
        Assert.NotEmpty(stackReconverges);

        var enterSet = stackEnters.ToHashSet();
        var closedSet = stackLeaves.Concat(stackReconverges).ToHashSet();
        Assert.All(stackLeaves, site => Assert.Contains(site, enterSet));
        Assert.All(stackEnters, site => Assert.Contains(site, closedSet));

        var maxSite = stackEnters.Concat(stackReconverges).Max();
        Assert.Equal(maxSite + 1, stackLowered.RegionCount);

        var loweredEnters = ExtractRegionSites(lowered.LoweredBody, GSharpInternalNames.Prelude.WarpRegionEnter, siteIndex: 2);
        var loweredLeaves = ExtractRegionSites(lowered.LoweredBody, GSharpInternalNames.Prelude.WarpRegionLeave, siteIndex: 2);
        var loweredArrives = ExtractRegionSites(lowered.LoweredBody, GSharpInternalNames.Prelude.WarpReconvergeArrive, siteIndex: 2);

        Assert.Equal(loweredEnters.OrderBy(static s => s), stackEnters.OrderBy(static s => s));
        Assert.Equal(loweredLeaves.OrderBy(static s => s), stackLeaves.OrderBy(static s => s));
        Assert.Equal(loweredArrives.OrderBy(static s => s), stackReconverges.OrderBy(static s => s));
    }

    private static int[] ExtractRegionSites(BlockSyntax body, string functionName, int siteIndex) =>
        body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(call => Calls(call, functionName))
            .Select(call => ExtractRegionSite(call, siteIndex))
            .ToArray();

    private static int ExtractRegionSite(InvocationExpressionSyntax call, int siteIndex)
    {
        Assert.True(call.ArgumentList.Arguments.Count > siteIndex,
            $"Expected at least {siteIndex + 1} arguments on {call}.");
        var siteExpression = call.ArgumentList.Arguments[siteIndex].Expression;
        var literal = Assert.IsType<LiteralExpressionSyntax>(siteExpression);
        return Assert.IsType<int>(literal.Token.Value);
    }

    private static (LoweredStateMachine Lowered, StackLoweredBody StackLowered) LowerFromTestCase(
        string methodName,
        bool isDeviceFunction)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", TestCase));
        var method = FindMethod(tree, methodName);
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method)
                     ?? throw new InvalidOperationException($"{methodName} has no symbol.");
        return (
            AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction),
            AstLoweringPipeline.LowerForStack(method, model, symbol, isDeviceFunction));
    }

    private static string[] GetCallsAfterReconvergencePass(LoweredStateMachine lowered) =>
        lowered.LoweredBody
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergePass))
            .Select(static pass => FirstUserCallAfter(pass))
            .OfType<string>()
            .ToArray();

    private static ExpressionStatementSyntax FindMarkStatement(BlockSyntax body, string markCall) =>
        body.DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Single(s => s.Expression is InvocationExpressionSyntax inv && Describe(inv) == markCall);

    private static SyntaxList<StatementSyntax> GetStatementList(SyntaxNode? parent) =>
        parent switch
        {
            BlockSyntax block => block.Statements,
            SwitchSectionSyntax section => section.Statements,
            _ => throw new InvalidOperationException($"Expected BlockSyntax or SwitchSectionSyntax, got {parent?.Kind()}.")
        };

    private static void AssertHasGateStackReconvergeBefore(BlockSyntax body, string markCall)
    {
        var markStatement = FindMarkStatement(body, markCall);
        var statements = GetStatementList(markStatement.Parent);
        var index = statements.IndexOf(markStatement);
        var reconvergeIndex = -1;
        for (var i = index - 1; i >= 0; i--)
        {
            if (statements[i] is ExpressionStatementSyntax
                {
                    Expression: InvocationExpressionSyntax call
                })
            {
                if (Calls(call, GSharpInternalNames.StackWarp.Reconverge))
                {
                    reconvergeIndex = i;
                    break;
                }

                if (Calls(call, GSharpInternalNames.StackWarp.RegionEnter))
                {
                    continue;
                }
            }

            break;
        }

        Assert.True(reconvergeIndex >= 0,
            $"Expected {GSharpInternalNames.StackWarp.Reconverge} (followed only by {GSharpInternalNames.StackWarp.RegionEnter}) immediately before {markCall}.");
    }

    private static void AssertHasPrecedingStackReconverge(BlockSyntax body, string markCall)
    {
        var markStatement = FindMarkStatement(body, markCall);
        var statements = GetStatementList(markStatement.Parent);
        var index = statements.IndexOf(markStatement);
        Assert.Contains(
            statements
                .Take(index)
                .Select(static s => (s as ExpressionStatementSyntax)?.Expression)
                .OfType<InvocationExpressionSyntax>(),
            static call => Calls(call, GSharpInternalNames.StackWarp.Reconverge));
    }

    private static void AssertHasLatchBeforeAllBackGotos(BlockSyntax body, string labelName, int expectedCount)
    {
        var backGotos = body
            .DescendantNodes()
            .OfType<GotoStatementSyntax>()
            .Where(g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: var target } &&
                        target == labelName)
            .ToArray();
        Assert.Equal(expectedCount, backGotos.Length);

        foreach (var backGoto in backGotos)
        {
            var statements = GetStatementList(backGoto.Parent);
            var index = statements.IndexOf(backGoto);
            Assert.True(index > 0, "Expected latch reconvergence call before backward goto.");
            var prevInvocation = Assert.IsType<ExpressionStatementSyntax>(statements[index - 1])
                .Expression as InvocationExpressionSyntax;
            Assert.NotNull(prevInvocation);
            Assert.True(Calls(prevInvocation, GSharpInternalNames.StackWarp.Reconverge),
                $"Expected {GSharpInternalNames.StackWarp.Reconverge} before backward goto, got {prevInvocation}.");
        }
    }
}
