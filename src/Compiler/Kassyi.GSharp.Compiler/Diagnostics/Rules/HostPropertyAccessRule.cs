namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Extensions;

/// <summary>Diagnostic rule that verifies accessed properties/fields are valid within a GPU kernel.</summary>
internal sealed class HostPropertyAccessRule
{
    public static readonly HostPropertyAccessRule Instance = new();

    private HostPropertyAccessRule()
    {
    }

    public void AnalyzeIdentifierName(IdentifierNameSyntax identifier, ref ValidationContext context) =>
        AnalyzeSymbol(identifier, ref context);

    public void AnalyzeMemberAccess(MemberAccessExpressionSyntax memberAccess, ref ValidationContext context) =>
        AnalyzeSymbol(memberAccess, ref context);

    private void AnalyzeSymbol(ExpressionSyntax node, ref ValidationContext context)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol;

        if (symbol is null)
        {
            return;
        }

        if (symbol is not (IPropertySymbol or IFieldSymbol) ||
            !symbol.IsStatic ||
            symbol is IFieldSymbol { IsConst: true } ||
            symbol.ContainingType?.TypeKind == TypeKind.Enum)
        {
            return;
        }

        if (symbol.ContainingType != null)
        {
            if (symbol.ContainingType.HasAttribute(KnownAttributes.GpuDeviceFunction) ||
                symbol.ContainingType.HasAttribute(KnownAttributes.GpuKernel))
            {
                return;
            }
        }

        // Allow Special types like ThreadIndex, BlockIndex
        if (symbol.ContainingType != null && symbol.ContainingNamespace.Name == "Core" &&
            symbol.ContainingNamespace.ContainingNamespace?.Name == "GSharp")
        {
            return; // Simplification, rely on actual definitions
        }

        var typeName = symbol.ContainingType?.Name ?? "";
        var syntaxName = string.IsNullOrEmpty(typeName) ? symbol.Name : $"{typeName}.{symbol.Name}";

        context.ReportDiagnostic(Diagnostic.Create(
            GSharpDiagnostics.UnsupportedSyntax,
            node.GetLocation(),
            syntaxName));
    }
}
