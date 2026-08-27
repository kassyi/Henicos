namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>Diagnostic rule that requires explicit type suffixes on implicit var declarations with numeric literals.</summary>
internal sealed class ImplicitVarNeedsSuffixRule
{
    public static readonly ImplicitVarNeedsSuffixRule Instance = new();

    private ImplicitVarNeedsSuffixRule()
    {
    }

    public void Analyze(VariableDeclaratorSyntax variable, ref ValidationContext context)
    {
        if (variable.Parent is not VariableDeclarationSyntax { Type.IsVar: true })
        {
            return;
        }

        if (variable.Initializer?.Value is not LiteralExpressionSyntax literal)
        {
            return;
        }

        if (!literal.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            return;
        }

        var text = literal.Token.Text;
        if (text.Contains('.') || text.Contains('e') || text.Contains('E'))
        {
            return;
        }

        var upper = text.ToUpperInvariant();
        if (upper.Contains("U") || upper.Contains("L"))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            GSharpDiagnostics.ImplicitVarNeedsSuffix,
            variable.GetLocation(),
            variable.Identifier.Text);

        context.ReportDiagnostic(diagnostic);
    }
}
