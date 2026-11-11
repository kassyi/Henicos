namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Fails a call or a method group whose target <see cref="CallTargetClassifier"/> rejects: reports the diagnostic of
/// the plan (GSH0107 or GSH0109) and writes the placeholder, so the device code never names a function that nothing
/// defines.
/// </summary>
internal static class CallTargetEmitter
{
    /// <summary>
    /// Reports and writes the placeholder when <paramref name="node"/>, a call of <paramref name="called"/> that
    /// reaches the standard emission, has a target that cannot be emitted.
    /// </summary>
    /// <returns>Whether it reported and wrote the placeholder.</returns>
    public static bool TryFailCall(SyntaxNode node, IMethodSymbol called, ref TranspileContext context)
    {
        var plan = CallTargetClassifier.Classify(called, node, context.Query.SemanticModel, context.Emitter.Dialect);
        if (!plan.IsFailure)
        {
            return false;
        }

        context.Reporter.Fail(plan.Descriptor, node, plan.MessageArgs);
        context.Visitor.AppendUnsupportedPlaceholder(node);
        return true;
    }

    /// <summary>
    /// Reports and writes the placeholder when <paramref name="node"/> names a method without calling it (a method
    /// group passed to a StackLinq stage or converted to a delegate) whose target cannot be emitted.
    /// </summary>
    /// <returns>Whether it reported and wrote the placeholder.</returns>
    public static bool TryFailMethodGroup(SyntaxNode node, ISymbol? symbol, ref TranspileContext context) =>
        symbol is IMethodSymbol method &&
        CallTargetClassifier.IsMethodGroup(node) &&
        TryFailCall(node, method, ref context);
}
