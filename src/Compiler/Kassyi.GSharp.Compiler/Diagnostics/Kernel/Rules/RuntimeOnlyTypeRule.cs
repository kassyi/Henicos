namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports each reference to a type that only the .NET runtime can give meaning to: <c>dynamic</c>,
/// <see cref="TypedReference"/>, <c>GCHandle</c> and <see langword="decimal"/> (GSH0003,
/// <see cref="GpuCodeConstraints.GetRuntimeOnlyTypeName"/>).
/// </summary>
/// <remarks>
/// The type is decided by the symbol that the name binds to, so <c>var</c> reports the type it stands for and a user
/// type with the same name is not reported. <c>__makeref</c> needs no rule: the transpiler has no rule for it, so it is
/// reported as an unsupported syntax kind. A <see langword="decimal"/> value needs no type name to reach device code:
/// the keyword, a literal (<c>1.5m</c>), a reference to a field, local, parameter or property of the type and a call that
/// returns one are reported as well.
/// </remarks>
internal sealed class RuntimeOnlyTypeRule :
    ISyntaxValidationRule<IdentifierNameSyntax>,
    ISyntaxValidationRule<PredefinedTypeSyntax>,
    ISyntaxValidationRule<LiteralExpressionSyntax>,
    ISyntaxValidationRule<InvocationExpressionSyntax>
{
    private const string DecimalName = "decimal";

    public static readonly RuntimeOnlyTypeRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(GSharpDiagnostics.UnsupportedSyntax);

    private RuntimeOnlyTypeRule()
    {
    }

    public void Analyze(IdentifierNameSyntax node, ref ValidationContext context)
    {
        var symbol = context.SemanticModel.GetSymbol(node);
        if (GpuCodeConstraints.GetRuntimeOnlyTypeName(symbol as ITypeSymbol) is { } name)
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node, name);
            return;
        }

        if (GetValueType(symbol) is { SpecialType: SpecialType.System_Decimal })
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node, DecimalName);
        }
    }

    public void Analyze(PredefinedTypeSyntax node, ref ValidationContext context)
    {
        if (node.Keyword.IsKind(SyntaxKind.DecimalKeyword))
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node, DecimalName);
        }
    }

    public void Analyze(LiteralExpressionSyntax node, ref ValidationContext context)
    {
        if (node.Token.Value is decimal)
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node, DecimalName);
        }
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetTypeSymbol(node) is { SpecialType: SpecialType.System_Decimal })
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node, DecimalName);
        }
    }

    private static ITypeSymbol? GetValueType(ISymbol? symbol) => symbol switch
    {
        IFieldSymbol field => field.Type,
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        IPropertySymbol property => property.Type,
        _ => null
    };
}
