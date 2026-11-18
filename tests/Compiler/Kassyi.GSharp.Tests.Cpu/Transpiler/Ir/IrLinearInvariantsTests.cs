#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies <see cref="IrInvariants.VerifyLinear"/> and linear-node rejection in <see cref="IrInvariants.Verify"/>.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrLinearInvariantsTests
{
    private const string Cases = "Ir/IrLinearCases.cs";
    private const string CasesType = "IrLinear.IrLinearCases";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void VerifyLinear_EveryCase_HasNoViolation()
    {
        var compilation = IrTestCases.Compile(Cases);
        var checkedCount = 0;

        foreach (var built in IrTestCases.BuildAll(Cases, CasesType))
        {
            var classified = IrClassifier.Classify(built, compilation);
            if (IrSplitLowering.Apply(classified, compilation, out _) is not { } lowered)
            {
                continue;
            }

            checkedCount++;
            var linear = IrLinearizer.Linearize(lowered, compilation);
            Assert.Empty(IrInvariants.VerifyLinear(linear, compilation));
        }

        Assert.True(checkedCount >= 10, $"Expected at least 10 lowered methods, got {checkedCount}.");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void VerifyLinear_GotoToAMissingLabel_ReportsIt()
    {
        var compilation = IrTestCases.Compile(Cases);
        var method = IrTestCases.Build(Cases, CasesType, "NoSplitIf");
        var missing = new IrLabelSymbol(IrLabelKind.BranchEnd, 0);
        var @goto = new IrGoto(missing, method.Body.Source.AsSynthesized());

        var violations = IrInvariants.VerifyLinear([@goto], compilation);

        Assert.Single(violations);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void VerifyLinear_OpaqueStatementInAKeptIf_ReportsIt()
    {
        var compilation = IrTestCases.Compile(Cases);
        var breakLoop = IrClassifier.Classify(IrTestCases.Build(Cases, CasesType, "BreakInLoop"), compilation);
        var opaque = IrNodeChildren.DescendantsAndSelf(breakLoop.Body).OfType<IrOpaqueStatement>().First();
        var noSplitIf = IrClassifier.Classify(IrTestCases.Build(Cases, CasesType, "NoSplitIf"), compilation);
        var keptIf = Assert.IsType<IrIf>(noSplitIf.Body.Statements[0]);
        var modifiedIf = keptIf.Update(keptIf.Condition, opaque, null);

        var violations = IrInvariants.VerifyLinear([modifiedIf], compilation);

        Assert.Single(violations);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Verify_LinearStatementInTheBuiltBody_ReportsIt()
    {
        var compilation = IrTestCases.Compile(Cases);
        var method = IrTestCases.Build(Cases, CasesType, "NoSplitIf");
        var label = new IrLabel(new IrLabelSymbol(IrLabelKind.BranchEnd, 0), method.Body.Source.AsSynthesized());
        var body = new IrBlock(method.Body.Statements.Add(label), method.Body.Locals, method.Body.Source);
        var modified = new IrMethod(method.Symbol, method.Parameters, method.This, body, method.Source);

        var violations = IrInvariants.Verify(modified, compilation);

        Assert.Single(violations);
    }
}
