namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>Diagnostic rule that requires explicit type suffixes on implicit var declarations with numeric literals.</summary>
internal sealed class ImplicitVarNeedsSuffixRule : ISyntaxValidationRule<VariableDeclaratorSyntax>
{
    public static readonly ImplicitVarNeedsSuffixRule Instance = new();
    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(GSharpDiagnostics.ImplicitVarNeedsSuffix);

    private ImplicitVarNeedsSuffixRule()
    {
    }

    public void Analyze(VariableDeclaratorSyntax variable, ref ValidationContext context)
    {
        if (variable.Parent is not VariableDeclarationSyntax { Type.IsVar: true } ||
            variable.Initializer?.Value is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            return;
        }

        var text = literal.Token.Text;
        if (text.IndexOf('.') >= 0 || text.IndexOf('e') >= 0 || text.IndexOf('E') >= 0)
        {
            return;
        }

        if (text.IndexOf('U') >= 0 || text.IndexOf('u') >= 0 || text.IndexOf('L') >= 0 || text.IndexOf('l') >= 0)
        {
            return;
        }

        context.Report(
            GSharpDiagnostics.ImplicitVarNeedsSuffix,
            variable,
            variable.Identifier.Text);
    }
}
