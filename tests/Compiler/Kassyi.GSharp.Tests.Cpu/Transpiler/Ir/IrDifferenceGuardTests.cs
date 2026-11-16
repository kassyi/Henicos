#nullable enable
using Kassyi.GSharp.Compiler.Analysis;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies the guards of the comparisons between the IR and the syntax passes: a known reason is given only where it
/// explains the difference, and only the statements of the IR are reported as missing a plan.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrDifferenceGuardTests
{
    private const string Cases = "Ir/IrClassifierCases.cs";
    private const string CasesType = "IrClassifierCases.Cases";

    // The syntax walk writes memory where the IR writes nothing
    private static readonly ExpressionEffects s_writesMemory = new(default, new VariableSet { Memory = true });

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Reason_DiscardOfACallThatWritesNothing_PutsTheMissingWriteDownToTheDiscard() =>
        Assert.Equal(IrClassificationEquivalence.KnownPrefix + "a discard is written by no one",
            DiscardReason("DiscardPure"));

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Reason_DiscardOfACallThatWritesMemory_GivesNoReason() =>
        Assert.Null(DiscardReason("DiscardWrite"));

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsIrStatement_StatementAfterAnOpaqueLoop_IsOne() =>
        Assert.True(IsIrStatement(SyntaxKind.SimpleAssignmentExpression));

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void IsIrStatement_StatementInsideAnOpaqueLoop_IsNotOne() =>
        Assert.False(IsIrStatement(SyntaxKind.InvocationExpression));

    // The reason for a write of memory that only the syntax walk finds in the discard assignment of case name
    private static string? DiscardReason(string name)
    {
        var compilation = IrTestCases.Compile(Cases);
        var assignment = IrClassifierTests.Single(IrClassifierTests.Classify(name));

        return IrEffectDifferences.Reason(assignment, IrEffectDifferences.Less, s_writesMemory, default, compilation);
    }

    // Whether the expression statement of BarrierInLoop whose expression has the kind is a statement of its IR: the
    // assignment after the loop, or the barrier inside it
    private static bool IsIrStatement(SyntaxKind kind)
    {
        var compilation = IrTestCases.Compile(Cases);
        var body = IrTestCases.FindMethod(compilation, CasesType, "BarrierInLoop")
            .DeclaringSyntaxReferences
            .Single()
            .GetSyntax();
        var statement = body.DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Single(node => node.Expression.IsKind(kind));

        return IrSpillEquivalence.IsIrStatement(IrClassifierTests.Build("BarrierInLoop"), statement.Span,
            body.SyntaxTree.GetRoot());
    }
}
