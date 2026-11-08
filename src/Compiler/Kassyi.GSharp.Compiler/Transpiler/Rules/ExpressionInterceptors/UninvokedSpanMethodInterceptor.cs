using Kassyi.GSharp.Compiler.Analysis;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionInterceptors;

/// <summary>
/// Fails a name or member access that refers to a span or array method without calling it (GSH0003,
/// <see cref="SpanWriteClassifier.TryFailUninvoked"/>, #731).
/// </summary>
internal sealed class UninvokedSpanMethodInterceptor : IExpressionInterceptor
{
    /// <inheritdoc />
    public bool TryAppend(ExpressionSyntax expression, ref TranspileContext context) =>
        expression is IdentifierNameSyntax or MemberAccessExpressionSyntax &&
        SpanWriteClassifier.TryFailUninvoked(expression, ref context);
}
