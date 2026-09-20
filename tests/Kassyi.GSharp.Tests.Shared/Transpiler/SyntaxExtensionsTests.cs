using Microsoft.CodeAnalysis.CSharp;
using Kassyi.GSharp.Compiler.Extensions;
using Xunit;

namespace Kassyi.GSharp.Tests.Transpiler;

public class SyntaxExtensionsTests
{
    [SkippableFact]
    [Trait("Category", "Compiler")]
    [Trait("FeatureId", "GEN-20")]
    public void Test_GEN20_SyntaxExtensions_SyntaxToken_IsKind_MatchesCorrectly()
    {
        var token = SyntaxFactory.Token(SyntaxKind.RefKeyword);

        Assert.True(token.IsKind(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword));
        Assert.False(token.IsKind(SyntaxKind.InKeyword, SyntaxKind.OutKeyword));
        Assert.False(token.IsKind());
    }

    [SkippableFact]
    [Trait("Category", "Compiler")]
    [Trait("FeatureId", "GEN-20")]
    public void Test_GEN20_SyntaxExtensions_SyntaxNode_IsKind_MatchesCorrectly()
    {
        var node = SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression);

        Assert.True(node.IsKind(SyntaxKind.NumericLiteralExpression, SyntaxKind.StringLiteralExpression));
        Assert.False(node.IsKind(SyntaxKind.TrueLiteralExpression, SyntaxKind.FalseLiteralExpression));
        Assert.False(node.IsKind());
    }
}
