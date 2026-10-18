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
}
