namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>
/// Writes the C# names of Roslyn symbols for generated C# source: type references from <c>global::</c>, namespace names
/// and the names that declare a type, each identifier that is a keyword escaped with <c>@</c> as
/// <c>SymbolDisplayFormat.FullyQualifiedFormat</c> writes it; and, apart from those, the metadata name of a namespace.
/// </summary>
internal static class CSharpTypeNameWriter
{
    /// <summary>
    /// Gets the name of <paramref name="namespaceSymbol"/> as C# source spells it (<c>@event.P3</c>), without
    /// <c>global::</c>, or an empty string for the global namespace.
    /// </summary>
    public static string GetCsharpNamespaceName(this INamespaceSymbol? namespaceSymbol) =>
        GetNamespaceName(namespaceSymbol, escapeKeywords: true);

    /// <summary>Appends the name that <see cref="GetCsharpNamespaceName"/> returns.</summary>
    public static ref readonly SourceWriter AppendCsharpNamespace(this in SourceWriter writer,
        INamespaceSymbol? namespaceSymbol)
    {
        AppendNamespace(writer, namespaceSymbol, escapeKeywords: true);
        return ref writer;
    }

    /// <summary>
    /// Appends the dotted namespace name <paramref name="namespaceName"/> (<c>event.P3</c>, read from a file rather
    /// than a symbol) as C# source spells it: each segment escaped as <see cref="AppendCsharpIdentifier"/> does
    /// (<c>@event.P3</c>).
    /// </summary>
    public static ref readonly SourceWriter AppendCsharpNamespace(this in SourceWriter writer, string namespaceName)
    {
        var start = 0;
        while (true)
        {
            var end = namespaceName.IndexOf('.', start);
            if (end < 0)
            {
                writer.AppendCsharpIdentifier(namespaceName[start..]);
                return ref writer;
            }

            writer.AppendCsharpIdentifier(namespaceName[start..end]);
            writer.Append('.');
            start = end + 1;
        }
    }

    /// <summary>
    /// Gets the metadata name of <paramref name="namespaceSymbol"/> (<c>event.P3</c>): dot-separated, keywords not
    /// escaped, or an empty string for the global namespace. For a metadata or display name only, such as the type name
    /// that <c>[UnsafeAccessorType]</c> takes; C# source takes <see cref="GetCsharpNamespaceName"/>.
    /// </summary>
    public static string GetMetadataNamespaceName(this INamespaceSymbol? namespaceSymbol) =>
        GetNamespaceName(namespaceSymbol, escapeKeywords: false);

    /// <summary>
    /// Appends <paramref name="name"/> as C# source spells an identifier: a reserved keyword gets the verbatim <c>@</c>
    /// prefix (<c>ISymbol.Name</c> of <c>@class</c> is <c>class</c>).
    /// </summary>
    public static ref readonly SourceWriter AppendCsharpIdentifier(this in SourceWriter writer, string name)
    {
        writer.AppendIf(IsReservedKeyword(name), "@");
        writer.Append(name);
        return ref writer;
    }

    /// <summary>
    /// Appends <paramref name="name"/> where it declares a type: a reserved keyword and a contextual keyword get the
    /// verbatim <c>@</c> prefix, since a type may not be named <c>required</c>, <c>scoped</c> or <c>file</c> (CS9029,
    /// CS9062, CS9056) and <c>@</c> is allowed before any identifier.
    /// </summary>
    public static ref readonly SourceWriter AppendCsharpTypeIdentifier(this in SourceWriter writer, string name)
    {
        writer.AppendIf(IsReservedKeyword(name) || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None, "@");
        writer.Append(name);
        return ref writer;
    }

    /// <summary>
    /// Appends the name that declares <paramref name="type"/> in a partial declaration of it: its identifier, escaped
    /// as <see cref="AppendCsharpTypeIdentifier"/> does, and its own type parameters, escaped as
    /// <see cref="AppendCsharpIdentifier"/> does (<c>@class</c>, <c>Outer&lt;T&gt;</c>).
    /// </summary>
    public static ref readonly SourceWriter AppendDeclaredTypeName(this in SourceWriter writer, INamedTypeSymbol type)
    {
        writer.AppendCsharpTypeIdentifier(type.Name);
        if (type.TypeParameters.IsEmpty)
        {
            return ref writer;
        }

        writer.Append('<');
        for (var i = 0; i < type.TypeParameters.Length; i++)
        {
            writer.AppendIf(i > 0, ", ");
            writer.AppendCsharpIdentifier(type.TypeParameters[i].Name);
        }

        writer.Append('>');
        return ref writer;
    }

    /// <summary>Gets the name that <see cref="AppendDeclaredTypeName"/> writes.</summary>
    public static string GetDeclaredTypeName(this INamedTypeSymbol type)
    {
        using var writer = new SourceWriter();
        writer.AppendDeclaredTypeName(type);
        return writer.ToString();
    }

    /// <summary>Whether <paramref name="name"/> is a reserved keyword, which C# source writes with <c>@</c>.</summary>
    public static bool IsReservedKeyword(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None;

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

    /// <summary>
    /// Returns the type that <see cref="AppendCsharpTypeName"/> writes as a syntax node, for a declaration that a
    /// rewriter builds into generated C#: fully qualified, so that no name of the user's scope (a type named
    /// <c>var</c> among them) changes what it binds to.
    /// </summary>
    public static TypeSyntax ToCsharpTypeSyntax(this ITypeSymbol type)
    {
        using var writer = new SourceWriter();
        writer.AppendCsharpTypeName(type);
        return SyntaxFactory.ParseTypeName(writer.ToString());
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
            case IPointerTypeSymbol or IFunctionPointerTypeSymbol:
                AppendPointerTypeName(writer, type, useKeywords);
                break;
            case ITypeParameterSymbol typeParameter:
                writer.AppendCsharpIdentifier(typeParameter.Name);
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

    // <pointed-at type>*, or a function pointer type
    private static void AppendPointerTypeName(in SourceWriter writer, ITypeSymbol type, bool useKeywords)
    {
        if (type is IFunctionPointerTypeSymbol functionPointer)
        {
            AppendFunctionPointer(writer, functionPointer.Signature, useKeywords);
            return;
        }

        if (type is IPointerTypeSymbol pointer)
        {
            AppendTypeName(writer, pointer.PointedAtType, useKeywords);
            writer.Append('*');
        }
    }

    // delegate*[ unmanaged[<conventions>]]<[<ref kind>] <parameter type>, ..., [ref [readonly]] <return type>>
    private static void AppendFunctionPointer(in SourceWriter writer, IMethodSymbol signature, bool useKeywords)
    {
        writer.Append("delegate*");
        AppendCallingConvention(writer, signature);
        writer.Append('<');
        foreach (var parameter in signature.Parameters)
        {
            writer.Append(parameter.RefKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                RefKind.RefReadOnlyParameter => "ref readonly ",
                _ => string.Empty,
            });
            AppendTypeName(writer, parameter.Type, useKeywords);
            writer.Append(", ");
        }

        writer.AppendIf(signature.ReturnsByRef, "ref ");
        writer.AppendIf(signature.ReturnsByRefReadonly, "ref readonly ");
        AppendTypeName(writer, signature.ReturnType, useKeywords);
        writer.Append('>');
    }

    private const string CallConvPrefix = "CallConv";

    // The calling convention of a function pointer as C# spells it: nothing for managed, ' unmanaged' with the
    // conventions in brackets (CallConvCdecl as Cdecl)
    private static void AppendCallingConvention(in SourceWriter writer, IMethodSymbol signature)
    {
        var convention = signature.CallingConvention switch
        {
            System.Reflection.Metadata.SignatureCallingConvention.CDecl => "Cdecl",
            System.Reflection.Metadata.SignatureCallingConvention.StdCall => "Stdcall",
            System.Reflection.Metadata.SignatureCallingConvention.ThisCall => "Thiscall",
            System.Reflection.Metadata.SignatureCallingConvention.FastCall => "Fastcall",
            _ => null,
        };
        if (convention is not null)
        {
            writer.Append($" unmanaged[{convention}]");
            return;
        }

        if (signature.CallingConvention != System.Reflection.Metadata.SignatureCallingConvention.Unmanaged)
        {
            return;
        }

        writer.Append(" unmanaged");
        var conventionTypes = signature.UnmanagedCallingConventionTypes;
        if (conventionTypes.IsEmpty)
        {
            return;
        }

        using (writer.Brackets())
        {
            for (var i = 0; i < conventionTypes.Length; i++)
            {
                writer.AppendIf(i > 0, ", ");
                var name = conventionTypes[i].Name;
                writer.Append(name.StartsWith(CallConvPrefix, StringComparison.Ordinal)
                    ? name[CallConvPrefix.Length..]
                    : name);
            }
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
                AppendNamespace(writer, containingNamespace, escapeKeywords: true);
                writer.Append('.');
                break;
        }

        writer.AppendCsharpIdentifier(type.Name);
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

    private static string GetNamespaceName(INamespaceSymbol? namespaceSymbol, bool escapeKeywords)
    {
        if (namespaceSymbol is null or { IsGlobalNamespace: true })
        {
            return string.Empty;
        }

        // Fast path for a single-level namespace that is not a keyword
        if (namespaceSymbol.ContainingNamespace is null or { IsGlobalNamespace: true } &&
            !(escapeKeywords && IsReservedKeyword(namespaceSymbol.Name)))
        {
            return namespaceSymbol.Name;
        }

        using var writer = new SourceWriter();
        AppendNamespace(writer, namespaceSymbol, escapeKeywords);
        return writer.ToString();
    }

    // With escapeKeywords, a segment that is a keyword is written as C# source spells it (@event)
    private static void AppendNamespace(in SourceWriter writer, INamespaceSymbol? ns, bool escapeKeywords)
    {
        if (ns is null || ns.IsGlobalNamespace)
        {
            return;
        }

        if (ns.ContainingNamespace is { IsGlobalNamespace: false } parentNs)
        {
            AppendNamespace(writer, parentNs, escapeKeywords);
            writer.Append('.');
        }

        if (escapeKeywords)
        {
            writer.AppendCsharpIdentifier(ns.Name);
        }
        else
        {
            writer.Append(ns.Name);
        }
    }
}
