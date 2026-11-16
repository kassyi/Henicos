using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Checks that the expansions of <see cref="PreLoweringExpander"/> map back to the macro statement they came from
/// (generator-invariants §2): the nodes each expansion builds carry the span of the statement, and every node of the
/// expansion is reported inside it.
/// </summary>
[Category(TestCategory.Compiler)]
public class PreLoweringExpanderSpanTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_ForUnroll_TheLoopMapsToTheMacroStatement()
    {
        var (model, statement, expansion) = Expand("ForUnrollBarrierKernel");
        var loop = Assert.IsType<ForStatementSyntax>(expansion);

        SyntaxNode[] built =
        [
            loop,
            Assert.IsType<VariableDeclarationSyntax>(loop.Declaration),
            Assert.IsAssignableFrom<ExpressionSyntax>(loop.Condition),
            loop.Incrementors.Single()
        ];
        Assert.All(built, node => Assert.Equal(statement.GetLocation(), model.GetOriginalLocation(node)));
        AssertReportedInside(model, statement, expansion);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_GridStrideLoop_TheLoopAndItsLocalsMapToTheMacroStatement()
    {
        var (model, statement, expansion) = Expand("GridStrideBarrierKernel");
        var block = Assert.IsType<BlockSyntax>(expansion);
        var loop = Assert.IsType<ForStatementSyntax>(block.Statements[2]);
        var loopBody = Assert.IsType<BlockSyntax>(loop.Statement);

        SyntaxNode[] built =
        [
            block,
            block.Statements[0],
            block.Statements[1],
            loop,
            Assert.IsType<VariableDeclarationSyntax>(loop.Declaration),
            Assert.IsAssignableFrom<ExpressionSyntax>(loop.Condition),
            loop.Incrementors.Single(),
            loopBody,
            loopBody.Statements[0]
        ];
        Assert.All(built, node => Assert.Equal(statement.GetLocation(), model.GetOriginalLocation(node)));
        AssertReportedInside(model, statement, expansion);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_SyncGridBodyThatReturns_TheSyncsAndGuardsMapBack()
    {
        var (model, statement, expansion) = Expand("SyncGridBodyKernel");
        var block = Assert.IsType<BlockSyntax>(expansion);
        var syncs = block.Statements
            .OfType<ExpressionStatementSyntax>()
            .Where(static s => s.Expression is InvocationExpressionSyntax { ArgumentList.Arguments.Count: 0 })
            .ToArray();
        var guards = block.Statements.OfType<IfStatementSyntax>().Skip(1).ToArray();

        Assert.NotEmpty(syncs);
        Assert.NotEmpty(guards);
        Assert.Equal(statement.GetLocation(), model.GetOriginalLocation(block));
        Assert.Equal(statement.GetLocation(), model.GetOriginalLocation(block.Statements[0]));
        Assert.All(syncs, sync => Assert.Equal(statement.GetLocation(), model.GetOriginalLocation(sync)));
        Assert.All(guards, guard => Assert.NotEqual(statement.GetLocation(), model.GetOriginalLocation(guard)));
        AssertReportedInside(model, statement, expansion);
    }

    // Every node of the expansion is reported (SemanticModelExtensions.GetReportLocation) inside the macro statement
    private static void AssertReportedInside(SemanticModel model, StatementSyntax statement, SyntaxNode expansion) =>
        Assert.All(
            expansion.DescendantNodesAndSelf(),
            node =>
            {
                var location = model.GetReportLocation(node);
                Assert.True(
                    location.SourceTree == statement.SyntaxTree && statement.Span.Contains(location.SourceSpan),
                    $"{node.Kind()} '{node}' is reported at {location}"
                );
            }
        );

    private static (SemanticModel Model, StatementSyntax Statement, StatementSyntax Expansion) Expand(string kernel)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", "PreLoweringMacroSpans.cs"));
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == kernel);
        var body = Assert.IsType<BlockSyntax>(method.Body);
        var annotated = TestSyncAnnotator.Annotate(LoweringAnnotations.AnnotateOriginalSpans(body, model.Compilation));

        var expanded = PreLoweringExpander.Expand(annotated, model);

        Assert.Equal(body.Statements.Count, expanded.Statements.Count);
        return (model, body.Statements[1], expanded.Statements[1]);
    }
}
