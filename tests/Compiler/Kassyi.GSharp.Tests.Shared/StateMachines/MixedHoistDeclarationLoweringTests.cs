using System.IO;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.ZeroAlloc.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests for a declaration whose declarators <see cref="ContextHoister"/> partly moves into the context (#1257):
/// the declarators run in the order written.
/// </summary>
[Category(TestCategory.Compiler)]
public class MixedHoistDeclarationLoweringTests
{
    // for (int a = 1, b = a + 1; ...) with 'a' hoisted: 'a' is assigned before 'b' reads it, in front of the loop
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Hoist_ForDeclarationWithLocalAfterHoisted_AssignsHoistedFirst()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", "MixedHoistForDemo.cs"));
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method);

        var lowered = AstLoweringPipeline.Lower(TestSyncAnnotator.Annotate(method), null, symbol,
            isDeviceFunction: false);

        var loop = lowered.LoweredBody.DescendantNodes().OfType<ForStatementSyntax>().Single();
        Assert.Null(loop.Declaration);
        Assert.Empty(loop.Initializers);
        var block = Assert.IsType<BlockSyntax>(loop.Parent);
        Assert.True(block.Statements.Count >= 3);
        var first = Assert.IsType<ExpressionStatementSyntax>(block.Statements[0]);
        var assignment = Assert.IsType<AssignmentExpressionSyntax>(first.Expression);
        Assert.Equal(SyntaxKind.SimpleAssignmentExpression, assignment.Kind());
        var pointerMember = Assert.IsType<MemberAccessExpressionSyntax>(assignment.Left);
        Assert.Equal(SyntaxKind.PointerMemberAccessExpression, pointerMember.Kind());
        Assert.Equal("__gsharp_ctx", Assert.IsType<IdentifierNameSyntax>(pointerMember.Expression).Identifier.ValueText);
        Assert.Equal("a", pointerMember.Name.Identifier.ValueText);
        var firstValue = Assert.IsType<LiteralExpressionSyntax>(assignment.Right);
        Assert.Equal(1, firstValue.Token.Value);

        var second = Assert.IsType<LocalDeclarationStatementSyntax>(block.Statements[1]);
        var declarator = Assert.Single(second.Declaration.Variables);
        Assert.Equal("b", declarator.Identifier.ValueText);
        Assert.NotNull(declarator.Initializer);
        var binary = Assert.IsType<BinaryExpressionSyntax>(declarator.Initializer.Value);
        Assert.Equal(SyntaxKind.AddExpression, binary.Kind());
        var binaryLeft = Assert.IsType<MemberAccessExpressionSyntax>(binary.Left);
        Assert.Equal(SyntaxKind.PointerMemberAccessExpression, binaryLeft.Kind());
        Assert.Equal("__gsharp_ctx", Assert.IsType<IdentifierNameSyntax>(binaryLeft.Expression).Identifier.ValueText);
        Assert.Equal("a", binaryLeft.Name.Identifier.ValueText);
        var binaryRight = Assert.IsType<LiteralExpressionSyntax>(binary.Right);
        Assert.Equal(1, binaryRight.Token.Value);

        Assert.Same(loop, block.Statements[2]);
    }
}
