namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>Writes C# type and namespace names of Roslyn symbols for generated C# source.</summary>
internal static class CSharpTypeNameWriter
{
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
        writer.AppendCsharpTypeNameWithoutAnnotation(type);
        if (type is { IsReferenceType: true, NullableAnnotation: NullableAnnotation.Annotated })
        {
            writer.Append('?');
        }

        return ref writer;
    }

    private static void AppendCsharpTypeNameWithoutAnnotation(this in SourceWriter writer, ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol arrayType:
                writer.AppendCsharpTypeName(arrayType.ElementType);
                writer.Append('[');
                for (var i = 0; i < arrayType.Rank - 1; i++)
                {
                    writer.Append(',');
                }

                writer.Append(']');
                return;
            case IPointerTypeSymbol pointerType:
                writer.AppendCsharpTypeName(pointerType.PointedAtType);
                writer.Append('*');
                return;
            case INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType:
                writer.AppendCsharpTypeName(nullableType.TypeArguments[0]);
                writer.Append('?');
                return;
            case INamedTypeSymbol { TypeArguments.Length: > 0 } genericType:
                writer.Append("global::");
                AppendContainingNamespaceOrType(writer, genericType.ContainingSymbol);
                writer.Append(genericType.Name);
                writer.Append('<');
                for (var i = 0; i < genericType.TypeArguments.Length; i++)
                {
                    if (i > 0)
                    {
                        writer.Append(", ");
                    }

                    writer.AppendCsharpTypeName(genericType.TypeArguments[i]);
                }

                writer.Append('>');
                return;
        }

        var keyword = type.SpecialType.GetCsharpKeyword();
        if (keyword != null)
        {
            writer.Append(keyword);
            return;
        }

        writer.AppendFullyQualifiedCsharpName(type);
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
