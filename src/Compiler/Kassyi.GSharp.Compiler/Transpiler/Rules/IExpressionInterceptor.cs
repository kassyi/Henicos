namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

/// <summary>
/// A special case that <see cref="RoslynToCudaVisitor.Visit"/> tries on every expression before the rule of its
/// <see cref="SyntaxKind"/>, in the order of <see cref="TranspilerRuleRegistry.ExpressionInterceptors"/>.
/// </summary>
/// <remarks>Implementations must be stateless and thread-safe.</remarks>
internal interface IExpressionInterceptor
{
    /// <summary>Writes <paramref name="expression"/> instead of its rule when the special case applies.</summary>
    /// <param name="expression">The expression about to be transpiled.</param>
    /// <param name="context">The active transpilation context.</param>
    /// <returns>
    /// <see langword="true"/> if the expression was written (or failed) here; otherwise, <see langword="false"/>.
    /// </returns>
    bool TryAppend(ExpressionSyntax expression, ref TranspileContext context);
}
