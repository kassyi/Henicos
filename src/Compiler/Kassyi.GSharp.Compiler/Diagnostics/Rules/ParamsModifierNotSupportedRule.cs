namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

internal sealed class ParamsModifierNotSupportedRule
{
    public static readonly ParamsModifierNotSupportedRule Instance = new();

    private ParamsModifierNotSupportedRule()
    {
    }

    public void Analyze(ParameterSyntax node, ref ValidationContext context)
    {
        if (node.Modifiers.Any(SyntaxKind.ParamsKeyword))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    GSharpDiagnostics.ParamsModifierNotSupported,
                    node.GetLocation(),
                    node.Identifier.Text,
                    context.MethodSymbol.Name));
        }
    }
}
