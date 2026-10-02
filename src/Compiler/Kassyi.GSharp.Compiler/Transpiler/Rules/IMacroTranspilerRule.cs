namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

/// <summary>
/// Defines an internal contract for intercepting and expanding high-level GPU macro invocations within expression statements.
/// </summary>
internal interface IMacroTranspilerRule
{
    /// <summary>
    /// Attempts to intercept and transpile the specified macro invocation statement.
    /// </summary>
    /// <param name="invocation">The invocation expression syntax node to evaluate.</param>
    /// <param name="statement">The containing expression statement syntax node.</param>
    /// <param name="symbol">The resolved method symbol of the invocation.</param>
    /// <param name="context">The active transpilation context.</param>
    /// <returns><see langword="true"/> if the macro was successfully intercepted and transpiled; otherwise, <see langword="false"/>.</returns>
    bool TryTranspile(
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        IMethodSymbol symbol,
        ref TranspileContext context);
}
