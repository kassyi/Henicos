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

    public static bool IsSystemLinq(this INamespaceSymbol? ns) =>
        ns is
        {
            Name: "Linq",
            ContainingNamespace:
            { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
        };

    public static bool IsSystemRuntimeCompilerServices(this INamespaceSymbol? ns) =>
        ns is
        {
            Name: "CompilerServices",
            ContainingNamespace:
            {
                Name: "Runtime",
                ContainingNamespace:
                { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
            }
        };

    public static bool IsSystemRuntimeInteropServices(this INamespaceSymbol? ns) =>
        ns is
        {
            Name: "InteropServices",
            ContainingNamespace:
            {
                Name: "Runtime",
                ContainingNamespace:
                { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
            }
        };

    /// <summary>Determines whether the namespace is exactly <c>System.Collections.Immutable</c> rooted at the global namespace.</summary>
    public static bool IsSystemCollectionsImmutable(this INamespaceSymbol? ns) =>
        ns is
        {
            Name: "Immutable",
            ContainingNamespace:
            {
                Name: "Collections",
                ContainingNamespace:
                { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
            }
        };

    /// <summary>
    /// Attempts to get the element type of an array-like type (T[], Span, ReadOnlySpan, Memory, ReadOnlyMemory, ArraySegment, ImmutableArray).
    /// </summary>
    public static bool TryGetArrayLikeElementType(this ITypeSymbol? type,
        [NotNullWhen(true)] out ITypeSymbol? elementType)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } genericType &&
            genericType.Name is TypeConstants.SpanType or TypeConstants.ReadOnlySpanType or "Memory" or "ReadOnlyMemory"
                or "ArraySegment" or "ImmutableArray" &&
            (genericType.ContainingNamespace.IsSystemNamespace() ||
             genericType.ContainingNamespace.IsSystemCollectionsImmutable()))
        {
            elementType = genericType.TypeArguments[0];
            return true;
        }

        elementType = null;
        return false;
    }

    /// <summary>
    /// Determines whether the type is an array-like type (T[], Span, ReadOnlySpan, Memory, ReadOnlyMemory, ArraySegment, ImmutableArray).
    /// </summary>
    public static bool IsArrayLikeType(this ITypeSymbol? type) => type.TryGetArrayLikeElementType(out _);

    /// <summary>
    /// Returns T when type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } and its single type argument is array-like; otherwise returns type unchanged.
    /// </summary>
    public static ITypeSymbol? UnwrapNullableArrayLike(this ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1
            } nullableType &&
            nullableType.TypeArguments[0].IsArrayLikeType())
        {
            return nullableType.TypeArguments[0];
        }

        return type;
    }

    /// <summary>
    /// Returns true exactly when UnwrapNullableArrayLike would unwrap.
    /// </summary>
    public static bool IsNullableArrayLike(this ITypeSymbol? type) =>
        type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1
        } nullableType &&
        nullableType.TypeArguments[0].IsArrayLikeType();

    public static bool IsCoreNamespace(this INamespaceSymbol? ns) =>
        ns is
        {
            Name: "Core",
            ContainingNamespace:
            { Name: "GSharp", ContainingNamespace: { Name: "Kassyi", ContainingNamespace.IsGlobalNamespace: true } }
        };

    /// <summary>
    /// Compares two symbols for equality using <see cref="SymbolEqualityComparer.Default"/>.
    /// </summary>
    public static bool IsEqualTo(this ISymbol? symbol, ISymbol? other) =>
        SymbolEqualityComparer.Default.Equals(symbol, other);

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
        if (typeSymbol.ContainingNamespace.IsCoreNamespace())
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
    /// Checks whether the type is Kassyi.GSharp.Core.SharedMemory&lt;T&gt;.
    /// </summary>
    public static bool IsSharedMemoryType(
        [NotNullWhen(true)] this ITypeSymbol? type) =>
        type is { Name: TypeConstants.SharedMemoryType } && type.ContainingNamespace.IsCoreNamespace();

    /// <summary>
    /// Attempts to get the first declaring syntax node of the specified type for this symbol.
    /// </summary>
    public static bool TryGetDeclaringSyntax<TSyntax>(
        this ISymbol? symbol,
        [NotNullWhen(true)] out TSyntax? syntax)
        where TSyntax : SyntaxNode
    {
        if (symbol is { DeclaringSyntaxReferences.Length: > 0 } &&
            symbol.DeclaringSyntaxReferences[0].GetSyntax() is TSyntax node)
        {
            syntax = node;
            return true;
        }

        syntax = null;
        return false;
    }

    /// <summary>
    /// Checks if the attribute matches the specified short name (e.g. "GpuKernel" matches both "GpuKernel" and "GpuKernelAttribute").
    /// Zero-allocation inspection using ReadOnlySpan.
    /// </summary>
    public static bool IsAttribute(this AttributeData attr, string shortName) =>
        KnownAttributes.IsAttributeName(attr.AttributeClass?.Name, shortName) &&
        KnownAttributes.IsDeclaredInOwnerNamespace(attr.AttributeClass?.ContainingNamespace, shortName);

    /// <summary>
    /// Accurately determines if the attribute is System.Runtime.InteropServices.StructLayoutAttribute.
    /// </summary>
    public static bool IsStructLayoutAttribute(this AttributeData attr)
    {
        return attr.IsAttribute(KnownAttributes.StructLayout) &&
               attr.AttributeClass?.ContainingNamespace.IsSystemRuntimeInteropServices() == true;
    }

    /// <summary>Gets a positive [StructLayout(Pack = n)] value; returns false when absent or 0.</summary>
    public static bool TryGetStructLayoutPack(this INamedTypeSymbol type, out int pack)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (!attr.IsStructLayoutAttribute())
            {
                continue;
            }

            foreach (var kvp in attr.NamedArguments)
            {
                if (kvp is { Key: "Pack", Value: { IsNull: false, Value: int p and > 0 } })
                {
                    pack = p;
                    return true;
                }
            }

            break;
        }

        pack = 0;
        return false;
    }

    /// <summary>
    /// Determines whether the symbol has an attribute matching the specified short name.
    /// </summary>
    public static bool HasAttribute(this ISymbol symbol, string shortName) =>
        symbol.GetAttributes()
            .AsStackEnumerable()
            .Any(shortName, static (a, name) => a.IsAttribute(name));

    /// <summary>
    /// Whether <paramref name="parameter"/> is a <c>[Constant]</c> parameter, which the launcher injects as a macro
    /// (<c>#define</c>) instead of passing it as an argument.
    /// </summary>
    public static bool IsConstantParameter(this IParameterSymbol parameter) =>
        parameter.HasAttribute(KnownAttributes.Constant);

    /// <summary>
    /// Gets the first attribute matching the specified short name, or null if none found.
    /// </summary>
    public static AttributeData? GetAttribute(this ISymbol symbol, string shortName) =>
        symbol.GetAttributes()
            .AsStackEnumerable()
            .FirstOrDefault(shortName, static (a, name) => a.IsAttribute(name));
}
