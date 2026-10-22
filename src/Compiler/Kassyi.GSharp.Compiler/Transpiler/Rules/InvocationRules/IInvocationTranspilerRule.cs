namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>
/// Defines an internal contract for intercepting and transpiling specific method invocation syntax nodes into target dialect expressions.
/// </summary>
internal interface IInvocationTranspilerRule
{
    /// <summary>
    /// Attempts to intercept and transpile the specified method invocation node into the transpilation context.
    /// </summary>
    /// <param name="node">The method invocation syntax node to evaluate.</param>
    /// <param name="context">The active transpilation context.</param>
    /// <param name="symbol">The resolved semantic method symbol.</param>
    /// <returns><see langword="true"/> if the rule intercepted and transpiled the invocation; otherwise, <see langword="false"/>.</returns>
    bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol);
}

