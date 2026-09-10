namespace Kassyi.GSharp.Compiler.Transpiler;

internal static class CppNameFormatter
{
    public static ref readonly SourceWriter AppendFullyQualifiedCppName(this in SourceWriter writer, ISymbol symbol)
    {
        if (symbol is ITypeParameterSymbol)
        {
            writer.Append(symbol.Name);
            return ref writer;
        }

        if (symbol is INamespaceSymbol { IsGlobalNamespace: true })
        {
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
        var sb = new System.Text.StringBuilder();
        AppendSafeIdentifierRecursive(symbol, sb);
        return sb.ToString();
    }

    private static void AppendSafeIdentifierRecursive(ISymbol symbol, System.Text.StringBuilder sb)
    {
        if (symbol is ITypeParameterSymbol)
        {
            sb.Append(symbol.Name);
            return;
        }

        if (symbol is INamespaceSymbol { IsGlobalNamespace: true })
        {
            return;
        }

        if (symbol.ContainingSymbol is { } parent && parent is not INamespaceSymbol { IsGlobalNamespace: true })
        {
            AppendSafeIdentifierRecursive(parent, sb);
            sb.Append('_');
        }

        sb.Append(symbol.Name);

        if (symbol is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            foreach (var typeArg in namedType.TypeArguments)
            {
                sb.Append('_');
                AppendSafeIdentifierRecursive(typeArg, sb);
            }
        }
    }
}
