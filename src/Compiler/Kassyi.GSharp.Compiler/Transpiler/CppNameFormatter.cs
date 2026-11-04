namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Formats Roslyn symbols and namespaces into safe, collision-free C++ identifiers and fully-qualified names.
/// </summary>
internal static class CppNameFormatter
{
    /// <summary>
    /// Appends the C++ name of a type without its template arguments: the encoded global name for a user struct
    /// (see <see cref="GlobalCppNames"/>), and the legacy <c>A_B_C</c> spelling that the prelude uses
    /// for other types (<c>System_Nullable</c>, shared memory).
    /// </summary>
    public static ref readonly SourceWriter AppendFullyQualifiedCppName(this in SourceWriter writer, ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol type && GlobalCppNames.IsEmittedUserType(type))
        {
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
}
