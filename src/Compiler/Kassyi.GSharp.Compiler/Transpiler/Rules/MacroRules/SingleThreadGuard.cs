namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// The guard of a <c>Gpu.SingleThread(body)</c> statement: the body runs only on the thread whose six indices
/// (<c>threadIdx</c> and <c>blockIdx</c>, x, y and z) are all 0, so it runs once per launch for every block and grid
/// shape (#690). The CUDA and native CPU emission (<see cref="SingleThreadMacroRule"/>) and the managed rewrite
/// (<see cref="ManagedExecutionRewriter"/>) take the condition from here.
/// </summary>
/// <remarks>
/// <c>Gpu.SingleThread</c> in <c>Kassyi.GSharp.Core</c> (the managed path of a device function that is not rewritten)
/// spells the same condition; the compiler does not reference it.
/// </remarks>
internal static class SingleThreadGuard
{
    /// <summary>
    /// Writes the C++ condition <c>(threadIdx.x|threadIdx.y|threadIdx.z|blockIdx.x|blockIdx.y|blockIdx.z)==0</c>.
    /// </summary>
    public static void AppendCppCondition(SourceWriter writer) =>
        writer.Append("(threadIdx.x|threadIdx.y|threadIdx.z|blockIdx.x|blockIdx.y|blockIdx.z)==0");

    /// <summary>
    /// Creates the managed condition
    /// <c>(ManagedGpuContext.ThreadIdxX | ... | ManagedGpuContext.BlockIdxZ) == 0</c>, each name qualified from
    /// <c>global::</c>. The node is built once and shared: syntax nodes are immutable, and a tree that takes one in
    /// gets its own copy.
    /// </summary>
    public static ExpressionSyntax CreateManagedCondition() => s_managedCondition;

    private static readonly ExpressionSyntax s_managedCondition = BuildManagedCondition();

    // (ManagedGpuContext.ThreadIdxX | ManagedGpuContext.ThreadIdxY | ManagedGpuContext.ThreadIdxZ |
    //  ManagedGpuContext.BlockIdxX | ManagedGpuContext.BlockIdxY | ManagedGpuContext.BlockIdxZ) == 0
    private static ExpressionSyntax BuildManagedCondition()
    {
        ReadOnlySpan<string> members =
        [
            ManagedNames.ThreadIdxXName,
            ManagedNames.ThreadIdxYName,
            ManagedNames.ThreadIdxZName,
            ManagedNames.BlockIdxXName,
            ManagedNames.BlockIdxYName,
            ManagedNames.BlockIdxZName
        ];
        ExpressionSyntax mask = ManagedNames.CreateMemberAccess(members[0]);
        for (var i = 1; i < members.Length; i++)
        {
            mask = SyntaxFactory.BinaryExpression(
                SyntaxKind.BitwiseOrExpression,
                mask,
                ManagedNames.CreateMemberAccess(members[i])
            );
        }

        return SyntaxFactory.BinaryExpression(
            SyntaxKind.EqualsExpression,
            SyntaxFactory.ParenthesizedExpression(mask),
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))
        );
    }
}
