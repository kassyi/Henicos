namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Formats Roslyn symbols and namespaces into safe, collision-free C++ identifiers and fully-qualified names.
/// </summary>
internal static class CppNameFormatter
{
    /// <summary>
    /// Appends the C++ name of a type without its template arguments: the encoded global name for a user struct and a
    /// type parameter (see <see cref="GlobalCppNames"/>), and the legacy <c>A_B_C</c> spelling that the prelude uses
    /// for other types (<c>System_Nullable</c>, shared memory).
    /// </summary>
    public static ref readonly SourceWriter AppendFullyQualifiedCppName(this in SourceWriter writer, ISymbol symbol)
    {
        switch (symbol)
        {
            case ITypeParameterSymbol parameter:
                writer.Append(GlobalCppNames.GetTypeParameterName(parameter));
                return ref writer;
            case INamedTypeSymbol type when GlobalCppNames.IsEmittedUserType(type):
                writer.Append(GlobalCppNames.GetTypeName(type));
                return ref writer;
        }

        return ref writer.AppendLegacyQualifiedName(symbol);
    }

    /// <summary>
    /// Returns the name used in the <c>#ifndef</c> guard of an emitted struct. The guard covers the definition, so the
    /// template arguments are not part of it.
    /// </summary>
    public static string GetIncludeGuardName(INamedTypeSymbol structSymbol)
    {
        using var writer = new SourceWriter();
        writer.Append("__GSHARP_STRUCT_").AppendFullyQualifiedCppName(structSymbol).Append("__");
        return writer.ToString();
    }

    private static ref readonly SourceWriter AppendLegacyQualifiedName(this in SourceWriter writer, ISymbol symbol)
    {
        if (symbol is INamespaceSymbol { IsGlobalNamespace: true })
        {
            return ref writer;
        }

        if (symbol.ContainingSymbol is { } parent and not INamespaceSymbol { IsGlobalNamespace: true })
        {
            writer.AppendLegacyQualifiedName(parent);
            writer.Append('_');
        }

        writer.Append(symbol.Name);
        return ref writer;
    }

    public static ref readonly SourceWriter AppendNestedCppName(this in SourceWriter writer, ISymbol symbol)
    {
        if (symbol.ContainingType is { } parentType)
        {
            writer.AppendNestedCppName(parentType);
            writer.Append('_');
        }

        writer.Append(symbol.Name);
        return ref writer;
    }

    public static ref readonly SourceWriter AppendAotStructPath(this in SourceWriter writer, ISymbol symbol)
    {
        if (symbol.ContainingType is { } parentType && CudaLayoutRules.IsCustomStruct(parentType))
        {
            writer.AppendAotStructPath(parentType);
            writer.Append('.');
        }
        else
        {
            writer.Append("AotContexts.");
        }

        writer.Append(TrimContextSuffix(symbol.Name));
        return ref writer;
    }

    public static string TrimContextSuffix(string name) => name.EndsWith("Context") ? name[..^7] : name;

    /// <summary>
    /// Determines whether <paramref name="identifier"/> is a C++20 keyword or alternative operator token,
    /// which the transpiler cannot emit verbatim as a C++ identifier.
    /// </summary>
    /// <remarks>
    /// C# keywords are included because verbatim identifiers (<c>@class</c>) carry the bare keyword text.
    /// </remarks>
    public static bool IsReservedCppKeyword(string identifier) => identifier is
        "alignas" or "alignof" or "and" or "and_eq" or "asm" or "auto" or "bitand" or "bitor" or "bool" or
        "break" or "case" or "catch" or "char" or "char8_t" or "char16_t" or "char32_t" or "class" or "compl" or
        "concept" or "const" or "consteval" or "constexpr" or "constinit" or "const_cast" or "continue" or
        "co_await" or "co_return" or "co_yield" or "decltype" or "default" or "delete" or "do" or "double" or
        "dynamic_cast" or "else" or "enum" or "explicit" or "export" or "extern" or "false" or "float" or "for" or
        "friend" or "goto" or "if" or "inline" or "int" or "long" or "mutable" or "namespace" or "new" or
        "noexcept" or "not" or "not_eq" or "nullptr" or "operator" or "or" or "or_eq" or "private" or
        "protected" or "public" or "register" or "reinterpret_cast" or "requires" or "return" or "short" or
        "signed" or "sizeof" or "static" or "static_assert" or "static_cast" or "struct" or "switch" or
        "template" or "this" or "thread_local" or "throw" or "true" or "try" or "typedef" or "typeid" or
        "typename" or "union" or "unsigned" or "using" or "virtual" or "void" or "volatile" or "wchar_t" or
        "while" or "xor" or "xor_eq";
}
