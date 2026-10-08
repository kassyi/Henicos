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
        AppendSymbolPath(symbol,
            appendName: static (ref SourceWriter w, string name) => w.Append(name),
            appendSeparator: static (ref SourceWriter w) => w.Append('_'),
            appendParent: static (ref SourceWriter w, ISymbol parent) => w.AppendFullyQualifiedCppName(parent),
            ref Unsafe.AsRef(in writer));
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
        AppendSymbolPath(symbol,
            appendName: static (ref StringBuilder b, string name) => b.Append(name),
            appendSeparator: static (ref StringBuilder b) => b.Append('_'),
            appendParent: static (ref StringBuilder b, ISymbol parent) => AppendSafeIdentifierRecursive(parent, b),
            ref sb);

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

    private delegate void AppendNameDelegate<TState>(ref TState state, string name);
    private delegate void AppendSeparatorDelegate<TState>(ref TState state);
    private delegate void AppendParentDelegate<TState>(ref TState state, ISymbol parent);

    private static void AppendSymbolPath<TState>(
        ISymbol symbol,
        AppendNameDelegate<TState> appendName,
        AppendSeparatorDelegate<TState> appendSeparator,
        AppendParentDelegate<TState> appendParent,
        ref TState state)
    {
        switch (symbol)
        {
            case ITypeParameterSymbol:
                appendName(ref state, symbol.Name);
                return;
            case INamespaceSymbol { IsGlobalNamespace: true }:
                return;
        }

        if (symbol.ContainingSymbol is { } parent and not INamespaceSymbol { IsGlobalNamespace: true })
        {
            appendParent(ref state, parent);
            appendSeparator(ref state);
        }

        appendName(ref state, symbol.Name);
    }

    public static ref readonly SourceWriter AppendNestedCppName(this in SourceWriter writer, ISymbol symbol)
    {
        AppendNestedSymbolPath(symbol,
            appendName: static (ref SourceWriter w, string name) => w.Append(name),
            appendSeparator: static (ref SourceWriter w) => w.Append('_'),
            appendParent: static (ref SourceWriter w, ISymbol parent) => w.AppendNestedCppName(parent),
            ref Unsafe.AsRef(in writer));
        return ref writer;
    }

    private static void AppendNestedSymbolPath<TState>(
        ISymbol symbol,
        AppendNameDelegate<TState> appendName,
        AppendSeparatorDelegate<TState> appendSeparator,
        AppendParentDelegate<TState> appendParent,
        ref TState state)
    {
        if (symbol.ContainingType is { } parentType)
        {
            appendParent(ref state, parentType);
            appendSeparator(ref state);
        }

        appendName(ref state, symbol.Name);
    }

    public static ref readonly SourceWriter AppendAotStructPath(this in SourceWriter writer, ISymbol symbol)
    {
        AppendAotStructPathRecursive(symbol,
            appendName: static (ref SourceWriter w, string name) => w.Append(name),
            appendSeparator: static (ref SourceWriter w) => w.Append('.'),
            appendParent: static (ref SourceWriter w, ISymbol parent) => w.AppendAotStructPath(parent),
            ref Unsafe.AsRef(in writer));
        return ref writer;
    }

    private static void AppendAotStructPathRecursive<TState>(
        ISymbol symbol,
        AppendNameDelegate<TState> appendName,
        AppendSeparatorDelegate<TState> appendSeparator,
        AppendParentDelegate<TState> appendParent,
        ref TState state)
    {
        if (symbol.ContainingType is { } parentType && Marshaling.CudaLayoutRules.IsCustomStruct(parentType))
        {
            appendParent(ref state, parentType);
            appendSeparator(ref state);
            appendName(ref state, TrimContextSuffix(symbol.Name));
        }
        else
        {
            appendName(ref state, "AotContexts." + TrimContextSuffix(symbol.Name));
        }
    }

    public static string TrimContextSuffix(string name)
    {
        return name.EndsWith("Context") ? name[..^7] : name;
    }
}
