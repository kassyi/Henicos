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

    public static ref readonly SourceWriter AppendConstantField(this in SourceWriter writer, IFieldSymbol field)
    {
        if (field.ConstantValue is null)
        {
            writer.Append("nullptr");
            return ref writer;
        }

        if (field.Type.SpecialType == SpecialType.System_Boolean)
        {
            writer.Append((bool)field.ConstantValue ? "true" : "false");
            return ref writer;
        }

        writer.Append(field.ConstantValue.ToString());

        var suffix = GetLiteralSuffix(field.Type.SpecialType);
        if (suffix != null)
        {
            writer.Append(suffix);
        }

        return ref writer;
    }

    private static string? GetLiteralSuffix(SpecialType specialType) => specialType switch
    {
        SpecialType.System_UInt64 => "ULL",
        SpecialType.System_UInt32 => "U",
        SpecialType.System_Int64 => "LL",
        SpecialType.System_Single => "f",
        SpecialType.System_Double => "d",
        SpecialType.System_Decimal => "m",
        _ => null
    };

    /// <summary>
    /// Returns the C# primitive keyword (e.g. "int", "float", "bool") for the specified <see cref="SpecialType"/>, or null if none.
    /// </summary>
    public static string? GetCsharpKeyword(this SpecialType specialType) => specialType switch
    {
        SpecialType.System_Boolean => "bool",
        SpecialType.System_Byte => "byte",
        SpecialType.System_SByte => "sbyte",
        SpecialType.System_Char => "char",
        SpecialType.System_Int16 => "short",
        SpecialType.System_UInt16 => "ushort",
        SpecialType.System_Int32 => "int",
        SpecialType.System_UInt32 => "uint",
        SpecialType.System_Int64 => "long",
        SpecialType.System_UInt64 => "ulong",
        SpecialType.System_Single => "float",
        SpecialType.System_Double => "double",
        SpecialType.System_Decimal => "decimal",
        SpecialType.System_String => "string",
        SpecialType.System_Object => "object",
        SpecialType.System_Void => "void",
        _ => null
    };

    /// <summary>
    /// Appends the C# type name to the source writer, using language keywords (e.g. "int", "float[]") for primitives
    /// and fully-qualified names for complex types.
    /// </summary>
    public static ref readonly SourceWriter AppendCsharpTypeName(this in SourceWriter writer, ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            writer.AppendCsharpTypeName(arrayType.ElementType);
            writer.Append("[]");
            return ref writer;
        }

        var keyword = type.SpecialType.GetCsharpKeyword();
        if (keyword != null)
        {
            writer.Append(keyword);
            return ref writer;
        }

        return ref writer.AppendFullyQualifiedCsharpName(type);
    }

    public static ref readonly SourceWriter AppendFullyQualifiedCsharpName(this in SourceWriter writer,
        ITypeSymbol symbol)
    {
        switch (symbol)
        {
            case IArrayTypeSymbol arrayType:
                writer.AppendFullyQualifiedCsharpName(arrayType.ElementType);
                writer.Append('[');
                for (var i = 0; i < arrayType.Rank - 1; i++)
                {
                    writer.Append(',');
                }

                writer.Append(']');
                return ref writer;

            case IPointerTypeSymbol pointerType:
                writer.AppendFullyQualifiedCsharpName(pointerType.PointedAtType);
                writer.Append('*');
                return ref writer;

            case ITypeParameterSymbol typeParam:
                writer.Append(typeParam.Name);
                return ref writer;
        }

        if (symbol is INamedTypeSymbol { TypeArguments.Length: > 0 } namedType)
        {
            writer.Append("global::");
            AppendContainingNamespaceOrType(writer, namedType.ContainingSymbol);
            writer.Append(namedType.Name);
            writer.Append('<');
            for (var i = 0; i < namedType.TypeArguments.Length; i++)
            {
                if (i > 0)
                {
                    writer.Append(", ");
                }

                writer.AppendFullyQualifiedCsharpName(namedType.TypeArguments[i]);
            }

            writer.Append('>');
            return ref writer;
        }

        if (symbol.TryGetDeclaringSyntax<BaseTypeDeclarationSyntax>(out var typeDecl))
        {
            writer.Append("global::");
            AppendParentNamespaceOrTypeFromAst(writer, typeDecl.Parent);
            writer.Append(typeDecl.Identifier.ValueText);
            return ref writer;
        }

        writer.Append("global::");
        AppendContainingNamespaceOrType(writer, symbol.ContainingSymbol);
        writer.Append(symbol.Name);
        return ref writer;
    }

    private static void AppendParentNamespaceOrTypeFromAst(in SourceWriter writer, SyntaxNode? parent)
    {
        switch (parent)
        {
            case null:
                return;
            case BaseNamespaceDeclarationSyntax ns:
                AppendParentNamespaceOrTypeFromAst(writer, ns.Parent);

                AppendNameSyntax(writer, ns.Name);

                writer.Append('.');
                break;
            case BaseTypeDeclarationSyntax parentType:
                AppendParentNamespaceOrTypeFromAst(writer, parentType.Parent);
                writer.Append(parentType.Identifier.ValueText);
                writer.Append('.');
                break;
        }
    }

    private static void AppendNameSyntax(in SourceWriter writer, NameSyntax name)
    {
        switch (name)
        {
            case QualifiedNameSyntax q:

                AppendNameSyntax(writer, q.Left);

                writer.Append('.');

                writer.Append(q.Right.Identifier.ValueText);
                break;
            case SimpleNameSyntax s:

                writer.Append(s.Identifier.ValueText);
                break;
        }
    }

    private static void AppendContainingNamespaceOrType(in SourceWriter writer, ISymbol? parent)
    {
        if (parent is null || parent is INamespaceSymbol { IsGlobalNamespace: true })
        {
            return;
        }

        AppendContainingNamespaceOrType(writer, parent.ContainingSymbol);
        writer.Append(parent.Name);
        writer.Append('.');
    }

    /// <summary>
    /// Recursively writes the namespace hierarchy to the source writer without string allocations.
    /// </summary>
    public static ref readonly SourceWriter AppendNamespace(this in SourceWriter writer, INamespaceSymbol? ns)
    {
        if (ns is null || ns.IsGlobalNamespace)
        {
            return ref writer;
        }

        if (ns.ContainingNamespace is { IsGlobalNamespace: false } parentNs)
        {
            writer.AppendNamespace(parentNs);
            writer.Append('.');
        }

        writer.Append(ns.Name);
        return ref writer;
    }
}
