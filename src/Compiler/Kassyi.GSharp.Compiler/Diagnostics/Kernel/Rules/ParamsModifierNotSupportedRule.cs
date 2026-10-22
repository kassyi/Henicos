namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

internal sealed class ParamsModifierNotSupportedRule : ISyntaxValidationRule<ParameterSyntax>
{
    public static readonly ParamsModifierNotSupportedRule Instance = new();
    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(GSharpDiagnostics.ParamsModifierNotSupported);

    private ParamsModifierNotSupportedRule()
    {
    }

    public void Analyze(ParameterSyntax node, ref ValidationContext context)
    {
        if (node.Modifiers.Any(SyntaxKind.ParamsKeyword))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.ParamsModifierNotSupported,
                node,
                node.Identifier.Text,
                context.MethodSymbol.Name);
        }
    }
}
