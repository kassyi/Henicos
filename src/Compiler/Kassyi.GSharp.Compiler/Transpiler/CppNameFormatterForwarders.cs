using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Forwards legacy <see cref="CppNameFormatter"/> extension calls from the syntax transpiler with
/// <see cref="GlobalCppNames"/> lookup.
/// </summary>
internal static class CppNameFormatterForwarders
{
    internal static readonly Func<INamedTypeSymbol, string?> TryGetUserTypeName =
        static type => GlobalCppNames.IsEmittedUserType(type) ? GlobalCppNames.GetTypeName(type) : null;

    /// <inheritdoc cref="CppNameFormatter.AppendFullyQualifiedCppName(in SourceWriter, ISymbol, Func{INamedTypeSymbol, string?}?)"/>
    public static ref readonly SourceWriter AppendFullyQualifiedCppName(
        this in SourceWriter writer,
        ISymbol symbol) =>
        ref CppNameFormatter.AppendFullyQualifiedCppName(in writer, symbol, TryGetUserTypeName);
}
