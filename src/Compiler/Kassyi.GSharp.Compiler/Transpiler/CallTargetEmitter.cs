namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Fails a call or a method group whose target <see cref="CallTargetClassifier"/> rejects, and a user-defined operator
/// without a device definition (<see cref="GpuCodeConstraints.GetOperatorProblem"/>): reports the diagnostic (GSH0107,
/// GSH0109 or GSH0060) and writes the placeholder, so the device code never names a function or an operator that
/// nothing defines.
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

        context.Reporter.Fail(plan.Descriptor, plan.ReportAt ?? node, plan.MessageArgs);
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

    /// <summary>
    /// Reports GSH0060 at the operator and writes the placeholder when <paramref name="node"/>, a binary, unary,
    /// increment or decrement expression or a compound assignment, calls a user-defined operator that device code has
    /// no definition of.
    /// </summary>
    /// <returns>Whether it reported and wrote the placeholder.</returns>
    public static bool TryFailOperator(ExpressionSyntax node, ref TranspileContext context)
    {
        // The operator token of the user's expression, which the analyzer reports, also for a node of a lowered tree
        if (GpuCodeConstraints.GetUserDefinedOperator(node, context.Query.SemanticModel) is not var (method, written) ||
            GpuCodeConstraints.GetOperatorProblem(method, context.Emitter.Dialect) is not var (name, type))
        {
            return false;
        }

        context.Reporter.Fail(
            GSharpDiagnostics.UserDefinedOperatorNotSupported,
            GetOperatorToken(written).GetLocation(),
            name,
            type);
        context.Visitor.AppendUnsupportedPlaceholder(node);
        return true;
    }

    private static SyntaxToken GetOperatorToken(ExpressionSyntax node) => node switch
    {
        BinaryExpressionSyntax binary => binary.OperatorToken,
        AssignmentExpressionSyntax assignment => assignment.OperatorToken,
        PrefixUnaryExpressionSyntax prefix => prefix.OperatorToken,
        PostfixUnaryExpressionSyntax postfix => postfix.OperatorToken,
        _ => throw new ArgumentException($"{node.Kind()} is not an operator expression.", nameof(node))
    };
}
