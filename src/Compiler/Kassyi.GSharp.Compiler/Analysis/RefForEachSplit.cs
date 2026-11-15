namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Whether the loop of a <c>foreach (ref ...)</c> holds a point where the CPU dialects split the state machine: the
/// one rule that the analyzer (GSH0039 at the loop variable) and the lowering (which holds the variable in the frame)
/// share.
/// </summary>
/// <remarks>
/// [WHY] The loop walks its source, a span or a temporary of one, across the split, which the frame cannot hold, so
/// the variable is held whether or not it is read after the split. A split point is what <c>HasSplitPoint</c> counts:
/// a block barrier, a warp shuffle or vote, <c>Gpu.SyncWarp</c>, a grid sync, or a call that may suspend. One in the
/// source expression runs before the first iteration, and one in a lambda or local function of the loop belongs to
/// that function (a barrier there is GSH0037), so neither counts.
/// </remarks>
internal static class RefForEachSplit
{
    /// <summary>
    /// Whether <paramref name="loop"/> declares its variable as a <c>ref</c> (<c>ref T</c>, <c>ref readonly T</c>).
    /// </summary>
    public static bool IsRefLoop(ForEachStatementSyntax loop) =>
        loop.Type is RefTypeSyntax or ScopedTypeSyntax { Type: RefTypeSyntax };

    /// <summary>Whether the body of <paramref name="loop"/> has a split point.</summary>
    /// <param name="loop">The loop.</param>
    /// <param name="model">The semantic model of the tree of the loop.</param>
    public static bool HoldsSplitPoint(ForEachStatementSyntax loop, SemanticModel model) =>
        loop.Statement
            .DescendantNodesAndSelf(static node =>
                node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .AsStackEnumerable()
            .OfType<InvocationExpressionSyntax>()
            .Any(model, static (invocation, m) => m.GetSymbol(invocation) is IMethodSymbol callee &&
                                                  (SyncIntrinsicClassifier.Classify(callee).SplitsStateMachine ||
                                                   SuspensionAnalysis.IsSuspendingFunction(callee, m.Compilation)));
}
