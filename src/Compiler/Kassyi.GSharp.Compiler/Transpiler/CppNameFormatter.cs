using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Compiler.Transpiler;

internal static class CppNameFormatter
{
    public static ref readonly SourceWriter AppendFullyQualifiedCppName(this in SourceWriter writer, ISymbol symbol)
    {
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
}
