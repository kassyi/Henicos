namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>Provides zero-allocation extension methods for Roslyn symbol inspections and namespace matching.</summary>
internal static class SymbolExtensions
{
    public static bool IsSystemNamespace(this INamespaceSymbol? ns) =>
        ns is { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true };

    public static bool IsSystemOrNumerics(this INamespaceSymbol? ns) =>
        ns is { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
            or
        {
            Name: TypeConstants.NumericsNamespace,
            ContainingNamespace:
            { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
        };

    public static bool IsSystemThreading(this INamespaceSymbol? ns) =>
        ns is
        {
            Name: TypeConstants.ThreadingNamespace,
            ContainingNamespace:
            { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
        };

    public static bool IsCoreNamespace(this INamespaceSymbol? ns) =>
        ns is
        {
            Name: "Core",
            ContainingNamespace:
            { Name: "GSharp", ContainingNamespace: { Name: "Kassyi", ContainingNamespace.IsGlobalNamespace: true } }
        };

    public static bool IsSystemNumericsVector(this ITypeSymbol? typeSymbol) =>
        typeSymbol is
        {
            Name: TypeConstants.Vector2Type or TypeConstants.Vector3Type or TypeConstants.Vector4Type,
            ContainingNamespace:
            {
                Name: TypeConstants.NumericsNamespace,
                ContainingNamespace:
                { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
            }
        };

    public static bool IsSystemValueTuple(this ITypeSymbol? typeSymbol) =>
        typeSymbol is INamedTypeSymbol { IsTupleType: true } ||
        (typeSymbol is INamedTypeSymbol { OriginalDefinition.Name: "ValueTuple" } named &&
         named.OriginalDefinition.ContainingNamespace.IsSystemNamespace());

    public static bool IsGpuBigIntRef(this ITypeSymbol? typeSymbol) =>
        typeSymbol is
        {
            Name: TypeConstants.GpuBigIntRefType,
            ContainingNamespace:
            {
                Name: "Core",
                ContainingNamespace:
                { Name: "GSharp", ContainingNamespace: { Name: "Kassyi", ContainingNamespace.IsGlobalNamespace: true } }
            }
        };

    /// <summary>
    /// Checks if the attribute matches the specified short name (e.g. "GpuKernel" matches both "GpuKernel" and "GpuKernelAttribute").
    /// Zero-allocation inspection using ReadOnlySpan.
    /// </summary>
    public static bool IsAttribute(this AttributeData attr, string shortName) =>
        KnownAttributes.IsAttributeName(attr.AttributeClass?.Name, shortName);

    /// <summary>
    /// Determines whether the symbol has an attribute matching the specified short name.
    /// </summary>
    public static bool HasAttribute(this ISymbol symbol, string shortName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.IsAttribute(shortName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the first attribute matching the specified short name, or null if none found.
    /// </summary>
    public static AttributeData? GetAttribute(this ISymbol symbol, string shortName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.IsAttribute(shortName))
            {
                return attr;
            }
        }

        return null;
    }
}
