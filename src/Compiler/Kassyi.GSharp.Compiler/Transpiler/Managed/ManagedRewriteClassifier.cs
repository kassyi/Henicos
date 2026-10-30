namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The managed-only semantic rewrites: the constructs whose meaning on the managed path comes from code that G# writes,
/// not from the C# the user wrote. <see cref="ManagedExecutionRewriter"/> rewrites a node only after this class says it
/// is one, and <c>ManagedVersionAnalysis</c> gives a device function a managed version exactly when its body (or a
/// function it calls) holds one, so the two cannot disagree (#711).
/// </summary>
/// <remarks>
/// <para>
/// The list is one case today: an operator macro assignment (<c>c = a + b;</c> with a <c>[GpuOperatorMacro]</c>
/// operator), whose operator body throws <c>GpuOnlyException</c> and has no run-time stand-in.
/// </para>
/// <para>
/// Not on the list, because the user's C# already gives the GPU result: the respellings of <c>ManagedNameQualifier</c>,
/// the site key of a <c>SharedMemory&lt;T&gt;</c> (its caller-info constructor computes the same key), the body macros
/// (<c>Gpu.ForUnroll</c>, <c>Gpu.GridStrideLoop</c>, <c>Gpu.SingleThread</c>, which Kassyi.GSharp.Core runs), intrinsics
/// whose stub forwards through the <c>managed:</c> key of the native API YAML, and barriers and warp exchanges, which
/// make a function suspend and give it a state machine. A new managed-only rewrite of the rewriter must be added here;
/// <c>ManagedRewriteConsistencyTests</c> fails otherwise.
/// </para>
/// </remarks>
internal static class ManagedRewriteClassifier
{
    /// <summary>Whether the managed path rewrites <paramref name="node"/> into code that G# writes.</summary>
    public static bool IsSemanticRewrite(SyntaxNode node, SemanticModel semanticModel) =>
        node is AssignmentExpressionSyntax assignment && TryGetOperatorMacro(assignment, semanticModel, out _);

    /// <summary>
    /// The operator macro that the managed path expands at <paramref name="assignment"/>: a simple assignment statement
    /// whose right side is an operator macro with a target (<see cref="OperatorMacroResolver.TryResolve"/>). A macro
    /// that cannot be expanded (GSH0003) is left as written.
    /// </summary>
    public static bool TryGetOperatorMacro(AssignmentExpressionSyntax assignment, SemanticModel semanticModel,
        out OperatorMacroResolver.OperatorMacro macro)
    {
        macro = default;
        return assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
               assignment.Parent is ExpressionStatementSyntax &&
               OperatorMacroResolver.TryResolve(assignment, semanticModel, out macro) &&
               macro.TargetMethod != null;
    }
}
