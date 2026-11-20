namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

/// <summary>
/// Checks and writes the index of a C# array element access, wrapping an array index that C# converts to
/// <see cref="ulong"/> in <c>__gsharp_array_index_u64</c> so an index above <see cref="long.MaxValue"/> throws
/// <see cref="OverflowException"/> when the index is evaluated (#1988, #2152).
/// </summary>
internal static class ArrayElementIndex
{
    /// <summary>
    /// Whether <paramref name="node"/> is a one-dimensional C# array element access whose index C# converts to
    /// <see cref="ulong"/> (a <see cref="ulong"/>, a <see cref="nuint"/>, or a type with an implicit conversion to
    /// <see cref="ulong"/>), which C# then converts with <c>conv.ovf.i.un</c> before checking the array for null or
    /// bounds.
    /// </summary>
    public static bool NeedsOverflowCheck(ElementAccessExpressionSyntax node, SemanticModel semanticModel) =>
        node.ArgumentList.Arguments is [{ RefKindKeyword.RawKind: (int)SyntaxKind.None, Expression: var index }] &&
        semanticModel.GetTypeSymbol(node.Expression) is IArrayTypeSymbol { Rank: 1 } &&
        semanticModel.GetConvertedTypeSymbol(index)?.SpecialType == SpecialType.System_UInt64;

    /// <summary>
    /// Writes <paramref name="index"/>, wrapped in <c>__gsharp_array_index_u64(...)</c> when it is the index of an
    /// array element access for which <see cref="NeedsOverflowCheck"/> holds.
    /// </summary>
    public static void Append(ExpressionSyntax index, ref TranspileContext context)
    {
        // index is the argument of element[index]: ArgumentSyntax in BracketedArgumentListSyntax
        if (index.Parent is ArgumentSyntax
            {
                Parent: BracketedArgumentListSyntax { Parent: ElementAccessExpressionSyntax element }
            } &&
            NeedsOverflowCheck(element, context.Query.SemanticModel))
        {
            context.Emitter.Writer.Append(GSharpInternalNames.Prelude.ArrayIndexU64);
            using (context.Emitter.Writer.Parentheses())
            {
                context.Visitor.Visit(index);
            }

            return;
        }

        context.Visitor.Visit(index);
    }
}
