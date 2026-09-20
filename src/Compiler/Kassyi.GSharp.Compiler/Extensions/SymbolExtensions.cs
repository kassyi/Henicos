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

    /// <summary>
    /// Gets the full namespace name without allocating via ToDisplayString().
    /// Uses StringBuilder to construct the dot-separated name from bottom to top.
    /// </summary>
    public static string GetFullNamespaceName(this INamespaceSymbol? namespaceSymbol)
    {
        if (namespaceSymbol == null || namespaceSymbol.IsGlobalNamespace)
        {
            return string.Empty;
        }

        // Fast path for single-level namespace
        if (namespaceSymbol.ContainingNamespace is null or { IsGlobalNamespace: true })
        {
            return namespaceSymbol.Name;
        }

        using var enumerator = getNamespaces(namespaceSymbol).GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return string.Empty;
        }

        var sb = new StringBuilder(enumerator.Current);
        while (enumerator.MoveNext())
        {
            sb.Insert(0, ".");
            sb.Insert(0, enumerator.Current);
        }

        return sb.ToString();

        static IEnumerable<string> getNamespaces(INamespaceSymbol? ns)
        {
            var current = ns;
            while (current != null && !current.IsGlobalNamespace)
            {
                yield return current.Name;
                current = current.ContainingNamespace;
            }
        }
    }

    /// <summary>
    /// Gets the fully qualified name (e.g. global::Namespace.TypeName) without allocating via ToDisplayString().
    /// </summary>
    public static string GetFullyQualifiedName(this ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
        {
            return string.Empty;
        }

        var typeName = typeSymbol.Name;
        // Handle generic types naively for typical GPU use-cases
        if (typeSymbol is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            typeName = $"{namedType.Name}<{string.Join(", ", namedType.TypeArguments.Select(t => t.Name))}>";
        }

        var ns = typeSymbol.ContainingNamespace.GetFullNamespaceName();
        return string.IsNullOrEmpty(ns) ? $"global::{typeName}" : $"global::{ns}.{typeName}";
    }
}
