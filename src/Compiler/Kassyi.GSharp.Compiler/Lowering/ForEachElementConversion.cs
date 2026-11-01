namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The conversion of each element that a <c>foreach</c> with an explicit loop variable type applies: C# converts every
/// element to the loop variable type with an explicit conversion (C# spec §13.9.5), which needs no cast in the source
/// (<c>foreach (int x in floats)</c>). The array lowering, the iterator inlining and the C++ range-for all ask here.
/// </summary>
internal static class ForEachElementConversion
{
    /// <summary>
    /// The loop variable type of <paramref name="node"/> when each element of <paramref name="elementType"/> is converted
    /// to it, or <see langword="null"/> for <c>var</c> and for a loop variable of the element type.
    /// </summary>
    public static ITypeSymbol? GetTarget(ForEachStatementSyntax node, ITypeSymbol? elementType, SemanticModel model)
    {
        if (node.Type.IsVar || elementType is null)
        {
            return null;
        }

        var target = model.GetDeclaredSymbol<ILocalSymbol>(node)?.Type ?? model.GetTypeSymbol(node.Type);
        return target is not null && !target.IsEqualTo(elementType) ? target : null;
    }
}
