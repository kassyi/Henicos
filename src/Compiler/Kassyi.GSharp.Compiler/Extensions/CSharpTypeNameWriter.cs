namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>Writes C# type and namespace names of Roslyn symbols for generated C# source.</summary>
internal static class CSharpTypeNameWriter
{
    /// <summary>
    /// Gets the dot-separated name of <paramref name="namespaceSymbol"/>, or an empty string for the global namespace.
    /// </summary>
    public static string GetFullNamespaceName(this INamespaceSymbol? namespaceSymbol)
    {
        if (namespaceSymbol is null or { IsGlobalNamespace: true })
        {
            return string.Empty;
        }

        // Fast path for single-level namespace
        if (namespaceSymbol.ContainingNamespace is null or { IsGlobalNamespace: true })
        {
            return namespaceSymbol.Name;
        }

        using var writer = new SourceWriter();
        writer.AppendNamespace(namespaceSymbol);
        return writer.ToString();
    }

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
    /// and fully-qualified names for complex types. Keywords are also used inside arrays, pointers, nullable value types,
    /// and type arguments (e.g. "int?[]", "float*", "global::System.Span&lt;int&gt;").
    /// </summary>
    /// <remarks>
    /// A nullable reference type keeps its <c>?</c> (e.g. "float[]?"), because generated code is compiled with nullable
    /// annotations enabled and dropping it makes every forwarded <c>null</c> a CS8604 warning.
    /// </remarks>
    public static ref readonly SourceWriter AppendCsharpTypeName(this in SourceWriter writer, ITypeSymbol type)
    {
        AppendTypeName(writer, type, useKeywords: true);
        return ref writer;
    }

    /// <summary>
    /// Appends the fully-qualified C# name of <paramref name="symbol"/> (e.g. "global::System.Int32[]"), without
    /// keywords or nullable annotations.
    /// </summary>
    public static ref readonly SourceWriter AppendFullyQualifiedCsharpName(this in SourceWriter writer,
        ITypeSymbol symbol)
    {
        AppendTypeName(writer, symbol, useKeywords: false);
        return ref writer;
    }

    /// <summary>Gets the name that <see cref="AppendFullyQualifiedCsharpName"/> writes.</summary>
    public static string GetFullyQualifiedCsharpName(this ITypeSymbol symbol)
    {
        using var writer = new SourceWriter();
        writer.AppendFullyQualifiedCsharpName(symbol);
        return writer.ToString();
    }

    // With keywords, primitives use their keyword, Nullable<T> becomes T? and a nullable reference type keeps its ?
    private static void AppendTypeName(in SourceWriter writer, ITypeSymbol type, bool useKeywords)
    {
        switch (type)
        {
            case IArrayTypeSymbol arrayType:
                AppendTypeName(writer, arrayType.ElementType, useKeywords);
                writer.Append('[');
                for (var i = 0; i < arrayType.Rank - 1; i++)
                {
                    writer.Append(',');
                }

                writer.Append(']');
                break;
            case IPointerTypeSymbol pointerType:
                AppendTypeName(writer, pointerType.PointedAtType, useKeywords);
                writer.Append('*');
                break;
            case ITypeParameterSymbol typeParameter:
                writer.Append(typeParameter.Name);
                break;
            case INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType
                when useKeywords:
                AppendTypeName(writer, nullableType.TypeArguments[0], useKeywords);
                writer.Append('?');
                break;
            case INamedTypeSymbol namedType when useKeywords && namedType.SpecialType.GetCsharpKeyword() is { } keyword:
                writer.Append(keyword);
                break;
            default:
                writer.Append("global::");
                AppendQualifiedName(writer, type, useKeywords);
                break;
        }

        if (useKeywords && type is { IsReferenceType: true, NullableAnnotation: NullableAnnotation.Annotated })
        {
            writer.Append('?');
        }
    }

    // <namespace>.<containing type>.<name>[<type arguments>]. A containing type keeps its type arguments, so a type
    // nested in a generic type is written as Outer<int>.Inner.
    private static void AppendQualifiedName(in SourceWriter writer, ITypeSymbol type, bool useKeywords)
    {
        switch (type.ContainingSymbol)
        {
            case INamedTypeSymbol containingType:
                AppendQualifiedName(writer, containingType, useKeywords);
                writer.Append('.');
                break;
            case INamespaceSymbol { IsGlobalNamespace: false } containingNamespace:
                writer.AppendNamespace(containingNamespace);
                writer.Append('.');
                break;
        }

        writer.Append(type.Name);
        if (type is not INamedTypeSymbol { TypeArguments: { Length: > 0 } typeArguments })
        {
            return;
        }

        writer.Append('<');
        for (var i = 0; i < typeArguments.Length; i++)
        {
            if (i > 0)
            {
                writer.Append(", ");
            }

            AppendTypeName(writer, typeArguments[i], useKeywords);
        }

        writer.Append('>');
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
