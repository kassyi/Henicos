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

        if (type is INamedTypeSymbol
            {
                IsGenericType: true, Name: TypeConstants.SpanType or TypeConstants.ReadOnlySpanType or "Memory"
                or "ReadOnlyMemory"
                or "ArraySegment" or "ImmutableArray"
            } genericType &&
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
    /// Whether <paramref name="type"/> is a primitive value type that device code supports: <c>bool</c>, <c>char</c>,
    /// the eight integer types, <c>float</c> and <c>double</c> (not <c>nint</c> / <c>nuint</c>, <c>decimal</c> or
    /// <c>Half</c>). The default sort order, the <c>[GpuConstexpr]</c> literals and the arithmetic conversions read it.
    /// </summary>
    public static bool IsPrimitiveValue(this SpecialType type) =>
        type == SpecialType.System_Boolean || type.IsArithmetic();

    /// <summary>
    /// Whether <paramref name="type"/> is an arithmetic type of C#: <see cref="IsPrimitiveValue"/> without <c>bool</c>.
    /// </summary>
    public static bool IsArithmetic(this SpecialType type) => type is
        SpecialType.System_Char or
        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or
        SpecialType.System_Int64 or SpecialType.System_UInt64 or
        SpecialType.System_Single or SpecialType.System_Double;

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
    /// Checks whether the type is one of the WMMA fragment types (<c>WmmaMatrixA&lt;...&gt;</c>,
    /// <c>WmmaMatrixB&lt;...&gt;</c>, or <c>WmmaAccumulator&lt;...&gt;</c>) in <c>Kassyi.GSharp.Core</c>.
    /// </summary>
    public static bool IsWmmaFragmentType(
        [NotNullWhen(true)] this ITypeSymbol? type) =>
        type is INamedTypeSymbol
        {
            Name: TypeConstants.WmmaTypes.MatrixA
                or TypeConstants.WmmaTypes.MatrixB
                or TypeConstants.WmmaTypes.Accumulator
        } && type.ContainingNamespace.IsCoreNamespace();

    /// <summary>
    /// Checks whether the type is a WMMA shape or layout marker type or interface (<c>IWmmaShape</c>,
    /// <c>IWmmaLayout</c>, <c>WmmaM16N16K16</c>, <c>WmmaRowMajor</c>, etc.) in <c>Kassyi.GSharp.Core</c>.
    /// </summary>
    public static bool IsWmmaMarkerType(
        [NotNullWhen(true)] this ITypeSymbol? type) =>
        type is INamedTypeSymbol
        {
            Name: TypeConstants.WmmaTypes.ShapeInterface
                or TypeConstants.WmmaTypes.M16N16K16
                or TypeConstants.WmmaTypes.M32N8K16
                or TypeConstants.WmmaTypes.M8N32K16
                or TypeConstants.WmmaTypes.M8N8K4
                or TypeConstants.WmmaTypes.LayoutInterface
                or TypeConstants.WmmaTypes.RowMajor
                or TypeConstants.WmmaTypes.ColumnMajor
        } && type.ContainingNamespace.IsCoreNamespace();

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
    /// Whether <paramref name="constructor"/> is the primary constructor of its type (<c>struct S(int a)</c>,
    /// <c>record struct R(int X)</c>), declared in source by the parameter list of the type declaration.
    /// </summary>
    public static bool IsPrimaryConstructor(this IMethodSymbol? constructor) =>
        constructor.TryGetPrimaryConstructorDeclaration(out _);

    /// <summary>
    /// Gets the type declaration whose parameter list declares <paramref name="constructor"/>, when it is a primary
    /// constructor declared in source.
    /// </summary>
    public static bool TryGetPrimaryConstructorDeclaration(this IMethodSymbol? constructor,
        [NotNullWhen(true)] out TypeDeclarationSyntax? declaration)
    {
        if (constructor is { MethodKind: MethodKind.Constructor, IsStatic: false, IsImplicitlyDeclared: false } &&
            constructor.TryGetDeclaringSyntax(out declaration) &&
            declaration.ParameterList != null)
        {
            return true;
        }

        declaration = null;
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

    /// <summary>
    /// Determines whether the symbol has an attribute matching the specified short name.
    /// </summary>
    public static bool HasAttribute(this ISymbol symbol, string shortName) =>
        symbol.GetAttributes()
            .AsStackEnumerable()
            .Any(shortName, static (a, name) => a.IsAttribute(name));

    /// <summary>
    /// Whether the locals of <paramref name="symbol"/> start zeroed, that is, no <c>[SkipLocalsInit]</c> applies to it.
    /// </summary>
    /// <remarks>
    /// The attribute is looked for in the order of Roslyn's <c>AreLocalsZeroed</c>: the method (and the property of an
    /// accessor), the methods around a local function or lambda, the containing types outward, then the module. A field
    /// or property (the enclosing symbol of an initializer) starts the search at its containing type. A null symbol
    /// counts as zeroed: ignoring the attribute is what the C# feature specification allows.
    /// </remarks>
    public static bool AreLocalsZeroed(this ISymbol? symbol)
    {
        for (var current = symbol;
             current is IMethodSymbol or IPropertySymbol or IFieldSymbol or INamedTypeSymbol;
             current = current.ContainingSymbol)
        {
            if (current.HasAttribute(KnownAttributes.SkipLocalsInit) ||
                current is IMethodSymbol { AssociatedSymbol: { } associated } &&
                associated.HasAttribute(KnownAttributes.SkipLocalsInit))
            {
                return false;
            }
        }

        return symbol?.ContainingModule?.HasAttribute(KnownAttributes.SkipLocalsInit) != true;
    }

    /// <summary>
    /// Whether <paramref name="parameter"/> is a <c>[GpuConstexpr]</c> parameter.
    /// </summary>
    public static bool IsConstexprParameter(this IParameterSymbol parameter) =>
        parameter.HasAttribute(KnownAttributes.GpuConstexpr);

    /// <summary>
    /// Whether <paramref name="method"/> is a <c>[GpuKernel(Cooperative = true)]</c> kernel, which the launcher starts
    /// with <c>cuLaunchCooperativeKernel</c> and which alone may call <c>Gpu.SyncGrid</c>.
    /// </summary>
    public static bool IsCooperativeKernel(this IMethodSymbol method) =>
        method.GetAttribute(KnownAttributes.GpuKernel) is { } kernelAttr &&
        kernelAttr.NamedArguments
            .AsStackEnumerable()
            .Any(static namedArg => namedArg is { Key: "Cooperative", Value.Value: true });

    /// <summary>
    /// Gets the first attribute matching the specified short name, or null if none found.
    /// </summary>
    public static AttributeData? GetAttribute(this ISymbol symbol, string shortName) =>
        symbol.GetAttributes()
            .AsStackEnumerable()
            .FirstOrDefault(shortName, static (a, name) => a.IsAttribute(name));

    /// <summary>
    /// The parameter of <paramref name="method"/> that the argument at <paramref name="index"/> of
    /// <paramref name="arguments"/> is passed to: by name, by position, or the <c>params</c> parameter for a trailing
    /// argument; <see langword="null"/> when none matches.
    /// </summary>
    public static IParameterSymbol? FindParameter(this IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments,
        int index)
    {
        var parameters = method.Parameters;
        if (arguments[index].NameColon is { } nameColon)
        {
            return parameters
                .AsStackEnumerable()
                .FirstOrDefault(nameColon.Name.Identifier.ValueText, static (p, name) => p.Name == name);
        }

        if (index < parameters.Length)
        {
            return parameters[index];
        }

        return parameters.Length > 0 && parameters[parameters.Length - 1].IsParams
            ? parameters[parameters.Length - 1]
            : null;
    }
}
