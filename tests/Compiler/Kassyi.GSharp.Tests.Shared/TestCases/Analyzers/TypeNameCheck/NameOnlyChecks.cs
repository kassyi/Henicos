using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler;

internal static class TypeConstants
{
    public const string GpuType = "Gpu";
    public const string WarpType = "Warp";
    public const string SpanType = "Span";
    public const string ReadOnlySpanType = "ReadOnlySpan";
    public const string CoreNamespace = "Kassyi.GSharp.Core";
}

internal static class KnownAttributes
{
    public const string GpuKernel = "GpuKernel";

    public static bool IsAttributeName(string? rawName, string shortName) =>
        rawName == shortName || rawName == shortName + "Attribute";
}

internal static class NameOnlyChecks
{
    // Reported: each compares a type name and never looks at the namespace.
    public static bool EqualsConstant(ITypeSymbol type) => type.Name == TypeConstants.GpuType;

    public static bool NotEqualsOnContainingType(IMethodSymbol method) => method.ContainingType.Name != TypeConstants.WarpType;

    public static bool EqualsLiteral(INamedTypeSymbol type) => type.Name == "Gpu";

    public static bool PropertyPattern(IMethodSymbol method) => method.ContainingType is { Name: TypeConstants.GpuType };

    public static bool IsPattern(ITypeSymbol type) => type.Name is TypeConstants.SpanType or TypeConstants.ReadOnlySpanType;

    public static int SwitchOnName(ISymbol symbol) => symbol.Name switch
    {
        TypeConstants.WarpType => 1,
        _ => 0
    };

    public static bool AttributeClassName(AttributeData attribute) => attribute.AttributeClass?.Name == "GpuKernelAttribute";

    public static bool AttributeShortName(AttributeData attribute) =>
        KnownAttributes.IsAttributeName(attribute.AttributeClass?.Name, KnownAttributes.GpuKernel);

    // Not reported: the member also checks the namespace.
    public static bool CheckedInSameCondition(ITypeSymbol type) =>
        type.Name == TypeConstants.GpuType && type.ContainingNamespace.ToDisplayString() == TypeConstants.CoreNamespace;

    public static bool CheckedByEarlyReturn(IMethodSymbol method)
    {
        if (!method.ContainingNamespace.IsCoreNamespace())
        {
            return false;
        }

        return method.ContainingType is { Name: TypeConstants.GpuType };
    }

    public static bool AttributeChecked(AttributeData attribute) =>
        KnownAttributes.IsAttributeName(attribute.AttributeClass?.Name, KnownAttributes.GpuKernel) &&
        IsDeclaredInOwnerNamespace(attribute.AttributeClass?.ContainingNamespace);

    // Not reported: not a type name, or not a symbol.
    public static bool MethodName(IMethodSymbol method) => method.Name == TypeConstants.GpuType;

    public static bool UnrelatedName(ITypeSymbol type) => type.Name == "Vector";

    public static bool SyntaxOnly(IdentifierNameSyntax identifier) => identifier.Identifier.ValueText == TypeConstants.GpuType;

    public static bool SyntaxOnlyAttribute(IdentifierNameSyntax identifier) =>
        KnownAttributes.IsAttributeName(identifier.Identifier.ValueText, KnownAttributes.GpuKernel);

    private static bool IsCoreNamespace(this INamespaceSymbol? ns) => ns?.ToDisplayString() == TypeConstants.CoreNamespace;

    private static bool IsDeclaredInOwnerNamespace(INamespaceSymbol? ns) => ns.IsCoreNamespace();
}
