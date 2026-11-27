namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionInterceptors;

/// <summary>
/// Writes the source of a lowered <c>foreach</c>, which C# checks for null at the loop entry, through the null check
/// (<see cref="ArrayNullCheckEmitter.TryAppendAnnotatedCheck"/>, #706).
/// </summary>
internal sealed class ArrayNullCheckInterceptor : IExpressionInterceptor
{
    /// <inheritdoc />
    public bool TryAppend(ExpressionSyntax expression, ref TranspileContext context) =>
        ArrayNullCheckEmitter.TryAppendAnnotatedCheck(expression, ref context);
}
