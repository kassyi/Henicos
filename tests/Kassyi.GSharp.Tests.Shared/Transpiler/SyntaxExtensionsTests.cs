using Microsoft.CodeAnalysis.CSharp;
using Kassyi.GSharp.Compiler.Extensions;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

public class SyntaxExtensionsTests
{
    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_SyntaxExtensions_SyntaxToken_IsKind_MatchesCorrectly()
    {
        var token = SyntaxFactory.Token(SyntaxKind.RefKeyword);

        Assert.True(token.IsKind(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword));
        Assert.True(token.IsKind(SyntaxKind.InKeyword, SyntaxKind.OutKeyword, SyntaxKind.RefKeyword));
        Assert.False(token.IsKind(SyntaxKind.InKeyword, SyntaxKind.OutKeyword));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_SyntaxExtensions_SyntaxNode_IsKind_MatchesCorrectly()
    {
        var node = SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression);

        Assert.True(node.IsKind(SyntaxKind.NumericLiteralExpression, SyntaxKind.StringLiteralExpression));
        Assert.True(node.IsKind(SyntaxKind.TrueLiteralExpression, SyntaxKind.FalseLiteralExpression, SyntaxKind.NumericLiteralExpression));
        Assert.False(node.IsKind(SyntaxKind.TrueLiteralExpression, SyntaxKind.FalseLiteralExpression));
    }
}
