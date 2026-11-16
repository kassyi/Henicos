using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Checks the flat goto sequence <see cref="BranchRewriter"/> makes of an if / else that holds a barrier: the labels in
/// the order the branches are visited (condition, then, else), and the conditions and statements it moves mapping back
/// to the user's source (generator-invariants §2).
/// </summary>
[Category(TestCategory.Compiler)]
public class BranchRewriterSpanTests
{
    private const string Prefix = GSharpInternalNames.Lowering.LabelPrefix;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Rewrite_NestedIfElse_NumbersTheBranchesInVisitOrder()
    {
        var (_, _, rewritten) = Rewrite();

        var labels = rewritten.Statements
            .OfType<LabeledStatementSyntax>()
            .ToArray();

        Assert.Equal(
            [
                $"{Prefix}BranchEnd_1",
                $"{Prefix}BranchElse_0",
                $"{Prefix}BranchEnd_2",
                $"{Prefix}BranchEnd_0"
            ],
            labels.Select(static l => l.Identifier.ValueText)
        );
        Assert.All(labels, static label => Assert.IsType<EmptyStatementSyntax>(label.Statement));
        Assert.Empty(rewritten.DescendantNodes().OfType<ElseClauseSyntax>());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Rewrite_NestedIfElse_MovedConditionsAndStatementsKeepTheirSpans()
    {
        var (model, method, rewritten) = Rewrite();
        var originalConditions = method.DescendantNodes()
            .OfType<IfStatementSyntax>()
            .Select(static i => i.Condition)
            .ToArray();
        var gotoConditions = rewritten.Statements
            .OfType<IfStatementSyntax>()
            .Select(static i => Assert.IsType<ParenthesizedExpressionSyntax>(
                Assert.IsType<PrefixUnaryExpressionSyntax>(i.Condition).Operand
            ).Expression)
            .ToArray();

        Assert.Equal(originalConditions.Length, gotoConditions.Length);
        Assert.All(
            originalConditions.Zip(gotoConditions),
            pair => Assert.Equal(pair.First.GetLocation(), model.GetOriginalLocation(pair.Second))
        );

        var originalAssignments = method.DescendantNodes().OfType<ExpressionStatementSyntax>().ToArray();
        var movedAssignments = rewritten.Statements.OfType<ExpressionStatementSyntax>().ToArray();
        Assert.Equal(originalAssignments.Length, movedAssignments.Length);
        Assert.All(
            originalAssignments.Zip(movedAssignments),
            pair => Assert.Equal(pair.First.GetLocation(), model.GetOriginalLocation(pair.Second))
        );
    }

    private static (SemanticModel Model, MethodDeclarationSyntax Method, BlockSyntax Rewritten) Rewrite()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", "NestedBranchBarriers.cs"));
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var body = Assert.IsType<BlockSyntax>(method.Body);
        var annotated = TestSyncAnnotator.Annotate(LoweringAnnotations.AnnotateOriginalSpans(body, model.Compilation));
        return (model, method, new BranchRewriter(model).Rewrite(annotated));
    }
}
