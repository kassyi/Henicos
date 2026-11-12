namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Writes a call or a method group whose target <see cref="CallTargetClassifier"/> does not let the standard emission
/// name: a target that cannot be emitted reports the diagnostic of the plan (GSH0107 or GSH0109) and writes the
/// placeholder, and a call of an intrinsic the dialect does not implement becomes a runtime failure (GSH0054, which the
/// analyzer reports), so the device code never names a function that nothing defines.
/// </summary>
internal static class CallTargetEmitter
{
    /// <summary>
    /// Writes <paramref name="node"/>, a call of <paramref name="called"/> that reaches the standard emission, when its
    /// target cannot be named: the placeholder of a failure, or the runtime failure of an intrinsic the dialect does
    /// not implement.
    /// </summary>
    /// <returns>Whether it wrote the call.</returns>
    public static bool TryReplaceCall(SyntaxNode node, IMethodSymbol called, ref TranspileContext context)
    {
        var plan = CallTargetClassifier.Classify(called, node, context.Query.SemanticModel, context.Emitter.Dialect);
        if (plan.Kind != CallTargetKind.UnsupportedIntrinsic)
        {
            return TryFail(node, plan, ref context);
        }

        AppendRuntimeFailure(called.ReturnType, plan, ref context);
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
        TryFail(
            node,
            CallTargetClassifier.Classify(method, node, context.Query.SemanticModel, context.Emitter.Dialect),
            ref context
        );

    private static bool TryFail(SyntaxNode node, CallTargetPlan plan, ref TranspileContext context)
    {
        if (!plan.IsFailure)
        {
            return false;
        }

        context.Reporter.Fail(plan.Descriptor, plan.ReportAt ?? node, plan.MessageArgs);
        context.Visitor.AppendUnsupportedPlaceholder(node);
        return true;
    }

    // __gsharp_unsupported("<method> is not implemented by the <dialect> dialect")
    // (__gsharp_unsupported("<method> is not implemented by the <dialect> dialect"), <return type>{})
    // [WHY] The arguments are not evaluated: the failure stops the launch, and the dialect has no definition to pass
    // them to. A call with a value gives the default of its type so that the expression around it still compiles.
    private static void AppendRuntimeFailure(ITypeSymbol returnType, CallTargetPlan plan, ref TranspileContext context)
    {
        var message = DialectCallSupport.GetNotImplementedMessage(plan.Method, plan.Dialect);
        var writer = context.Emitter.Writer;
        if (returnType.SpecialType == SpecialType.System_Void)
        {
            writer.Append($"{GSharpInternalNames.Prelude.Unsupported}(\"{message}\")");
            return;
        }

        using (writer.Parentheses())
        {
            writer.Append($"{GSharpInternalNames.Prelude.Unsupported}(\"{message}\"), ");
            context.Emitter.Dialect.AppendType(ref context, returnType);
            writer.Append("{}");
        }
    }
}
