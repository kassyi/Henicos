#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Reconverging;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

namespace Kassyi.GSharp.Tests.Cpu.Transpiler.Ir;

/// <summary>Verifies <see cref="IrReconvergedInvariants"/> negative cases.</summary>
[Category(TestCategory.Compiler)]
public class IrReconvergedInvariantsTests
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void VerifyReconverged_ArriveRemoved_ReportsRc3()
    {
        var steps = IrLoweringSteps.Run(IrLoweringSteps.Cases, IrLoweringSteps.CasesType, "ThreadVariantIf",
            isDeviceFunction: false);
        var statements = steps.Reconverged.Body.Body.Statements;
        var filtered = statements
            .Where(static s => s is not IrExpressionStatement
            {
                Expression: IrRuntimeCall { Function: IrRuntimeFunction.WarpReconvergeArrive }
            })
            .ToImmutableArray();
        Assert.Equal(statements.Length - 1, filtered.Length);
        var brokenBlock = steps.Reconverged.Body.Body.Update(filtered);
        var broken = new IrReconvergedBody(
            new IrSplitLoweredBody(brokenBlock, steps.Reconverged.Body.LoopConditions),
            steps.Reconverged.RegionCount);

        var violations = IrReconvergedInvariants.Verify(broken, steps.Compilation,
            isDeviceFunction: false, reachesActiveMask: true);

        Assert.Contains(violations, static v => v.StartsWith("RC3 ", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void VerifyReconverged_LeavesInTheWrongOrder_ReportsRc5()
    {
        var steps = IrLoweringSteps.Run(IrLoweringSteps.Cases, IrLoweringSteps.CasesType, "LeaveBothRegions",
            isDeviceFunction: true);
        var brokenBlock = new LeaveSwapper().VisitBlock(steps.Reconverged.Body.Body);
        var broken = new IrReconvergedBody(
            new IrSplitLoweredBody(brokenBlock, steps.Reconverged.Body.LoopConditions),
            steps.Reconverged.RegionCount);

        var violations = IrReconvergedInvariants.Verify(broken, steps.Compilation,
            isDeviceFunction: true, reachesActiveMask: true);

        Assert.Contains(violations, static v => v.StartsWith("RC5 ", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void VerifyReconverged_UnclassifiedIf_ReportsRc1()
    {
        var compilation = IrTestCases.Compile(IrLoweringSteps.Cases);
        var built = IrTestCases.Build(IrLoweringSteps.Cases, IrLoweringSteps.CasesType, "ThreadVariantIf");
        var classified = IrClassifier.Classify(built, compilation);
        var lowered = IrSplitLowering.Apply(classified, compilation, out var failure);
        Assert.Equal(IrSplitLoweringFailureKind.None, failure.Kind);
        Assert.NotNull(lowered);
        var unclassified = new IrReconvergedBody(lowered, 0);

        var violations = IrReconvergedInvariants.Verify(unclassified, compilation,
            isDeviceFunction: false, reachesActiveMask: true);

        Assert.Contains(violations, static v => v.StartsWith("RC1 ", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void VerifyReconverged_DeviceReturnDirectlyInsideRegion_ReportsRc5()
    {
        var steps = IrLoweringSteps.Run(IrLoweringSteps.Cases, IrLoweringSteps.CasesType,
            "IfAsLastStatementDevice", isDeviceFunction: true);
        var statements = steps.Reconverged.Body.Body.Statements;
        var enterIndex = -1;
        for (var index = 0; index < statements.Length; index++)
        {
            if (statements[index] is IrExpressionStatement
                {
                    Expression: IrRuntimeCall { Function: IrRuntimeFunction.WarpRegionEnter }
                })
            {
                enterIndex = index;
                break;
            }
        }

        Assert.True(enterIndex >= 0 && enterIndex + 2 < statements.Length);
        var replaced = statements.SetItem(enterIndex + 1, new IrReturn(null, statements[enterIndex + 1].Source));
        var brokenBlock = steps.Reconverged.Body.Body.Update(replaced);
        var broken = new IrReconvergedBody(
            new IrSplitLoweredBody(brokenBlock, steps.Reconverged.Body.LoopConditions),
            steps.Reconverged.RegionCount);

        var violations = IrReconvergedInvariants.Verify(broken, steps.Compilation,
            isDeviceFunction: true, reachesActiveMask: true);

        Assert.Contains(violations, static v => v.StartsWith("RC5 ", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void VerifyReconverged_ReusedRuntimeCallInsideExpression_ReportsRc6()
    {
        var steps = IrLoweringSteps.Run(IrLoweringSteps.Cases, IrLoweringSteps.CasesType, "ThreadVariantIf",
            isDeviceFunction: false);
        var statements = steps.Reconverged.Body.Body.Statements;
        var declaration = Assert.IsType<IrLocalDeclaration>(statements[0]);
        var enterCall = statements
            .OfType<IrExpressionStatement>()
            .Select(static s => s.Expression)
            .OfType<IrRuntimeCall>()
            .First(static call => call.Function == IrRuntimeFunction.WarpRegionEnter);
        var brokenDeclaration = declaration.Update(enterCall);
        var brokenBlock = steps.Reconverged.Body.Body.Update(statements.SetItem(0, brokenDeclaration));
        var broken = new IrReconvergedBody(
            new IrSplitLoweredBody(brokenBlock, steps.Reconverged.Body.LoopConditions),
            steps.Reconverged.RegionCount);

        var violations = IrReconvergedInvariants.Verify(broken, steps.Compilation,
            isDeviceFunction: false, reachesActiveMask: true);

        Assert.Contains(violations, static v => v.StartsWith("RC6 ", StringComparison.Ordinal));
    }

    private sealed class LeaveSwapper : IrRewriter
    {
        public override IrStatement VisitStatement(IrStatement statement)
        {
            var visited = base.VisitStatement(statement);
            if (visited is not IrBlock block)
            {
                return visited;
            }

            for (var index = 0; index + 1 < block.Statements.Length; index++)
            {
                if (block.Statements[index] is IrExpressionStatement
                    {
                        Expression: IrRuntimeCall { Function: IrRuntimeFunction.WarpRegionLeave }
                    } first &&
                    block.Statements[index + 1] is IrExpressionStatement
                    {
                        Expression: IrRuntimeCall { Function: IrRuntimeFunction.WarpRegionLeave }
                    } second)
                {
                    var swapped = block.Statements.SetItem(index, second).SetItem(index + 1, first);
                    return block.Update(swapped);
                }
            }

            return block;
        }
    }
}
