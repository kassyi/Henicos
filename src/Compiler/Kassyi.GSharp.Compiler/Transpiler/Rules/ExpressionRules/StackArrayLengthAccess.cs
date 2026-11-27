namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Writes <c>s.Length</c> of a stack array local as the size that its declaration gives.
/// </summary>
internal static class StackArrayLengthAccess
{
    // s.Length of a stack array local: the size its declaration gives.
    internal static bool TryTranspile(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        if (!StackArraySizeQuery.TryGetStackArraySize(node, context.Query.SemanticModel, out var size))
        {
            return false;
        }

        WriteStackArraySize(size, ref context);
        return true;
    }

    internal static bool TryGetStackArraySize(MemberAccessExpressionSyntax node, SemanticModel semanticModel,
        out StackArraySize size) =>
        StackArraySizeQuery.TryGetStackArraySize(node, semanticModel, out size);

    /// <summary>
    /// The size that the declaration of <paramref name="local"/> gives, for its <c>Length</c>: the syntax path writes
    /// it in place of the member.
    /// </summary>
    internal static bool TryGetStackArraySize(ILocalSymbol local, Compilation compilation, out StackArraySize size) =>
        StackArraySizeQuery.TryGetStackArraySize(local, compilation, out size);

    private static void WriteStackArraySize(in StackArraySize size, ref TranspileContext context)
    {
        if (size.Count is { } count)
        {
            context.Emitter.Writer.Append(count);
            return;
        }

        var expression = size.Expression ??
                         throw new InvalidOperationException("Stack array size without a count or an expression.");
        context.Visitor.Visit(expression);
    }
}
