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

    public static bool IsSystemNumericsVector(this ITypeSymbol? typeSymbol)
    {
        if (typeSymbol?.ContainingNamespace == null)
        {
            return false;
        }

        // BCL System.Numerics.Vector*
        if (typeSymbol.Name is TypeConstants.Vector2Type or TypeConstants.Vector3Type or TypeConstants.Vector4Type &&
            typeSymbol.ContainingNamespace.Name == TypeConstants.NumericsNamespace &&
            typeSymbol.ContainingNamespace.ContainingNamespace is
                { Name: TypeConstants.SystemNamespace, IsGlobalNamespace: false } sysNs &&
            sysNs.ContainingNamespace.IsGlobalNamespace)
        {
            return true;
        }

        // Kassyi.GSharp.Core Vector Types
        if (typeSymbol.ContainingNamespace is
            {
                Name: "Core",
                ContainingNamespace:
                { Name: "GSharp", ContainingNamespace: { Name: "Kassyi", ContainingNamespace.IsGlobalNamespace: true } }
            })
        {
            return typeSymbol.Name switch
            {
                "Float2" or "Float3" or "Float4" => true,
                "Int2" or "Int3" or "Int4" => true,
                "UInt2" or "UInt3" or "UInt4" => true,
                "Double2" or "Double3" or "Double4" => true,
                "Long2" or "Long3" or "Long4" => true,
                "ULong2" or "ULong3" or "ULong4" => true,
                _ => false
            };
        }

        return false;
    }


    /// <summary>
    /// Checks if the attribute matches the specified short name (e.g. "GpuKernel" matches both "GpuKernel" and "GpuKernelAttribute").
    /// Zero-allocation inspection using ReadOnlySpan.
    /// </summary>
    public static bool IsAttribute(this AttributeData attr, string shortName) =>
        KnownAttributes.IsAttributeName(attr.AttributeClass?.Name, shortName);

    /// <summary>
    /// Accurately determines if the attribute is System.Runtime.InteropServices.StructLayoutAttribute.
    /// </summary>
    public static bool IsStructLayoutAttribute(this AttributeData attr)
    {
        var name = attr.AttributeClass?.Name;
        if (name != "StructLayoutAttribute" && name != "StructLayout")
        {
            return false;
        }

        var ns = attr.AttributeClass?.ContainingNamespace;
        return ns is
        {
            Name: "InteropServices",
            ContainingNamespace:
            { Name: "Runtime", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
        };
    }

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
            while (current is { IsGlobalNamespace: false })
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

        var sb = new StringBuilder();
        BuildFullyQualifiedName(typeSymbol, sb);
        return sb.ToString();
    }

    private static void BuildFullyQualifiedName(ITypeSymbol symbol, StringBuilder sb)
    {
        switch (symbol)
        {
            case IArrayTypeSymbol arrayType:
            {
                BuildFullyQualifiedName(arrayType.ElementType, sb);
                sb.Append('[');
                for (var i = 0; i < arrayType.Rank - 1; i++)
                {
                    sb.Append(',');
                }

                sb.Append(']');
                return;
            }
            case IPointerTypeSymbol pointerType:
                BuildFullyQualifiedName(pointerType.PointedAtType, sb);
                sb.Append('*');
                return;
            case ITypeParameterSymbol typeParam:
                sb.Append(typeParam.Name);
                return;
        }

        var ns = symbol.ContainingNamespace.GetFullNamespaceName();
        if (!string.IsNullOrEmpty(ns))
        {
            sb.Append("global::").Append(ns).Append('.');
        }
        else if (symbol.ContainingNamespace?.IsGlobalNamespace == true)
        {
            sb.Append("global::");
        }

        BuildContainingTypes(symbol.ContainingType, sb);

        sb.Append(symbol.Name);

        if (symbol is not INamedTypeSymbol { IsGenericType: true } namedType)
        {
            return;
        }

        sb.Append('<');
        for (var i = 0; i < namedType.TypeArguments.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            BuildFullyQualifiedName(namedType.TypeArguments[i], sb);
        }

        sb.Append('>');
    }

    private static void BuildContainingTypes(INamedTypeSymbol? containingType, StringBuilder sb)
    {
        if (containingType == null)
        {
            return;
        }

        BuildContainingTypes(containingType.ContainingType, sb);

        sb.Append(containingType.Name);
        if (containingType.IsGenericType)
        {
            sb.Append('<');
            for (var i = 0; i < containingType.TypeArguments.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                BuildFullyQualifiedName(containingType.TypeArguments[i], sb);
            }

            sb.Append('>');
        }

        sb.Append('.');
    }
}
