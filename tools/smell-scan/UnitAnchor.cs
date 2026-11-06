using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Gives the position that identifies a measured unit across <c>#if</c> variants: the name token of a member (or the
/// arrow of a lambda), which stays put when an attribute, a modifier or a parameter type is inside a conditional block.
/// The range a finding reports can differ between variants; this position does not.
/// </summary>
internal static class UnitAnchor
{
    /// <summary>Returns the identifying position of <paramref name="node"/>.</summary>
    public static int Of(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => method.Identifier.SpanStart,
        ConstructorDeclarationSyntax constructor => constructor.Identifier.SpanStart,
        DestructorDeclarationSyntax destructor => destructor.Identifier.SpanStart,
        OperatorDeclarationSyntax op => op.OperatorToken.SpanStart,
        ConversionOperatorDeclarationSyntax conversion => conversion.OperatorKeyword.SpanStart,
        PropertyDeclarationSyntax property => property.Identifier.SpanStart,
        IndexerDeclarationSyntax indexer => indexer.ThisKeyword.SpanStart,
        EventDeclarationSyntax eventDeclaration => eventDeclaration.Identifier.SpanStart,
        LocalFunctionStatementSyntax local => local.Identifier.SpanStart,
        AccessorDeclarationSyntax accessor => accessor.Keyword.SpanStart,
        LambdaExpressionSyntax lambda => lambda.ArrowToken.SpanStart,
        AnonymousMethodExpressionSyntax anonymous => anonymous.DelegateKeyword.SpanStart,
        ArrowExpressionClauseSyntax arrow => arrow.ArrowToken.SpanStart,
        TypeDeclarationSyntax type => type.Keyword.SpanStart,
        _ => node.SpanStart,
    };
}
