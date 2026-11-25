namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// Formats Roslyn symbols and namespaces into safe, collision-free C++ identifiers and fully-qualified names.
/// </summary>
internal static class CppNameFormatter
{
    /// <summary>
    /// Appends the C++ name of a type without its template arguments: the encoded global name when
    /// <paramref name="tryGetUserTypeName"/> returns one, and the legacy <c>A_B_C</c> spelling that the prelude uses
    /// for other types (<c>System_Nullable</c>, shared memory).
    /// </summary>
    public static ref readonly SourceWriter AppendFullyQualifiedCppName(
        this in SourceWriter writer,
        ISymbol symbol,
        Func<INamedTypeSymbol, string?>? tryGetUserTypeName = null)
    {
        if (symbol is INamedTypeSymbol type && tryGetUserTypeName?.Invoke(type) is { } userTypeName)
        {
            writer.Append(userTypeName);
            return ref writer;
        }

        return ref writer.AppendLegacyQualifiedName(symbol);
    }

    /// <summary>
    /// Returns the name used in the <c>#ifndef</c> guard of an emitted struct. The guard covers the definition, so the
    /// template arguments are not part of it.
    /// </summary>
    public static string GetIncludeGuardName(
        INamedTypeSymbol structSymbol,
        Func<INamedTypeSymbol, string?>? tryGetUserTypeName = null)
    {
        using var writer = new SourceWriter();
        writer.Append("__GSHARP_STRUCT_")
            .AppendFullyQualifiedCppName(structSymbol, tryGetUserTypeName)
            .Append("__");
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
}
