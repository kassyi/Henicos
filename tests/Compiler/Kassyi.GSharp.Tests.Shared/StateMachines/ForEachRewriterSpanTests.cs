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
/// Checks that every node <see cref="ForEachRewriter"/> builds for an array <c>foreach</c> maps back to the user's
/// <c>foreach</c> statement (generator-invariants §2), and that the body it moves keeps its own spans.
/// </summary>
[Category(TestCategory.Compiler)]
public class ForEachRewriterSpanTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Rewrite_ArrayForEach_EveryBuiltNodeMapsToTheForEach()
    {
        var (model, forEach, rewritten) = Rewrite();
        var loop = rewritten.DescendantNodes().OfType<ForStatementSyntax>().Single();
        var source = rewritten.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .Single(static s => s.Declaration.Variables[0].Identifier.ValueText
                .StartsWith(GSharpInternalNames.Lowering.ForEachSourcePrefix, System.StringComparison.Ordinal));
        var item = Assert.IsType<LocalDeclarationStatementSyntax>(
            Assert.IsType<BlockSyntax>(loop.Statement).Statements[0]
        );
        var itemDeclarator = item.Declaration.Variables[0];
        var elementAccess = Assert.IsType<EqualsValueClauseSyntax>(itemDeclarator.Initializer).Value;

        SyntaxNode[] built =
        [
            source,
            loop,
            Assert.IsType<VariableDeclarationSyntax>(loop.Declaration),
            Assert.IsAssignableFrom<ExpressionSyntax>(loop.Condition),
            loop.Incrementors.Single(),
            loop.Statement,
            item,
            itemDeclarator,
            elementAccess
        ];
        Assert.All(built, node => Assert.Equal(forEach.GetLocation(), model.GetOriginalLocation(node)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Rewrite_ArrayForEach_MovedBodyKeepsItsOwnSpans()
    {
        var (model, forEach, rewritten) = Rewrite();
        var originalStatement = Assert.IsType<BlockSyntax>(forEach.Statement).Statements[0];
        var loop = rewritten.DescendantNodes().OfType<ForStatementSyntax>().Single();
        var movedStatement = Assert.IsType<BlockSyntax>(loop.Statement).Statements[1];

        Assert.Equal(originalStatement.ToString(), movedStatement.ToString());
        Assert.Equal(originalStatement.GetLocation(), model.GetOriginalLocation(movedStatement));
    }

    private static (SemanticModel Model, ForEachStatementSyntax ForEach, BlockSyntax Rewritten) Rewrite()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", "ForEachBarrierSpans.cs"));
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var forEach = method.DescendantNodes().OfType<ForEachStatementSyntax>().Single();
        var body = Assert.IsType<BlockSyntax>(method.Body);
        var annotated = TestSyncAnnotator.Annotate(LoweringAnnotations.AnnotateOriginalSpans(body, model.Compilation));
        return (model, forEach, new ForEachRewriter(model).Rewrite(annotated));
    }
}
