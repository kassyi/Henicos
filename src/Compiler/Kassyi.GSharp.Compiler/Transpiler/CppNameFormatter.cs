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

        var parent = symbol.ContainingSymbol;
        if (parent is not null and not INamespaceSymbol { IsGlobalNamespace: true })
        {
            writer.AppendFullyQualifiedCppName(parent);
            writer.Append('_');
        }

        writer.Append(symbol.Name);
        return ref writer;
    }

    public static string ToSafeCppIdentifier(this ISymbol symbol)
    {
        var sb = new StringBuilder();
        AppendSafeIdentifierRecursive(symbol, sb);
        return sb.ToString();
    }

    private static void AppendSafeIdentifierRecursive(ISymbol symbol, StringBuilder sb)
    {
        switch (symbol)
        {
            case ITypeParameterSymbol:
                sb.Append(symbol.Name);
                return;
            case INamespaceSymbol { IsGlobalNamespace: true }:
                return;
        }

        if (symbol.ContainingSymbol is { } parent and not INamespaceSymbol { IsGlobalNamespace: true })
        {
            AppendSafeIdentifierRecursive(parent, sb);
            sb.Append('_');
        }

        sb.Append(symbol.Name);

        if (symbol is not INamedTypeSymbol { IsGenericType: true } namedType)
        {
            return;
        }

        foreach (var typeArg in namedType.TypeArguments)
        {
            sb.Append('_');
            AppendSafeIdentifierRecursive(typeArg, sb);
        }
    }
}
