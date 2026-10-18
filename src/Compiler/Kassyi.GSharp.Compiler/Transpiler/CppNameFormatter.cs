namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Formats Roslyn symbols and namespaces into safe, collision-free C++ identifiers and fully-qualified names.
/// </summary>
internal static class CppNameFormatter
{
    /// <summary>
    /// Appends a sanitized, fully-qualified C++ identifier for the given symbol to the source writer.
    /// </summary>
    public static ref readonly SourceWriter AppendFullyQualifiedCppName(this in SourceWriter writer, ISymbol symbol)
    {
        switch (symbol)
        {
            case ITypeParameterSymbol:
                writer.Append(symbol.Name);
                return ref writer;
            case INamespaceSymbol { IsGlobalNamespace: true }:
                return ref writer;
        }

        if (symbol.ContainingSymbol is { } parent and not INamespaceSymbol { IsGlobalNamespace: true })
        {
            writer.AppendFullyQualifiedCppName(parent);
            writer.Append('_');
        }

        writer.Append(symbol.Name);
        return ref writer;
    }

    public static string ToSafeCppIdentifier(this ISymbol symbol)
    {
        using var writer = new SourceWriter();
        AppendSafeIdentifierRecursive(writer, symbol);
        return writer.ToString();
    }

    private static void AppendSafeIdentifierRecursive(in SourceWriter writer, ISymbol symbol)
    {
        switch (symbol)
        {
            case ITypeParameterSymbol:
                writer.Append(symbol.Name);
                return;
            case INamespaceSymbol { IsGlobalNamespace: true }:
                return;
        }

        if (symbol.ContainingSymbol is { } parent and not INamespaceSymbol { IsGlobalNamespace: true })
        {
            AppendSafeIdentifierRecursive(writer, parent);
            writer.Append('_');
        }

        writer.Append(symbol.Name);

        if (symbol is not INamedTypeSymbol { IsGenericType: true } namedType)
        {
            return;
        }

        foreach (var typeArg in namedType.TypeArguments)
        {
            writer.Append('_');
            AppendSafeIdentifierRecursive(writer, typeArg);
        }
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
