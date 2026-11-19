namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Decides where a call that splits the CPU state machine may stand, and provides the shared GSH0062 callee texts for
/// a suspending device function and a warp intrinsic.
/// </summary>
internal static class SplitPointPlacement
{
    /// <summary>The GSH0062 callee description for a device function that reaches a barrier or warp shuffle.</summary>
    public const string SuspendingDeviceFunctionCallee =
        "is a device function that reaches Gpu.SyncThreads() or a warp shuffle";

    /// <summary>The GSH0062 callee description for <c>Gpu.SyncGrid</c>.</summary>
    public const string GridBarrierCallee = "is a grid barrier";

    private const string WarpShuffleCallee = "is a warp shuffle";

    private const string WarpVoteCallee = "is a warp vote or reduction";

    private const string WarpBarrierCallee = "is a warp barrier";

    private const string WarpActiveMaskCallee = "returns the lanes of the warp that reach it together";

    /// <summary>The GSH0062 callee description for a warp intrinsic that splits the state machine.</summary>
    public static string GetWarpCallee(WarpShuffleKind kind) =>
        kind switch
        {
            WarpShuffleKind.SyncWarp => WarpBarrierCallee,
            WarpShuffleKind.ActiveMask => WarpActiveMaskCallee,
            _ when IsVoteOrReduction(kind) => WarpVoteCallee,
            _ => WarpShuffleCallee
        };

    private static bool IsVoteOrReduction(WarpShuffleKind kind) =>
        kind is WarpShuffleKind.Ballot or WarpShuffleKind.All or WarpShuffleKind.Any or WarpShuffleKind.ReduceAdd
            or WarpShuffleKind.ReduceMin or WarpShuffleKind.ReduceMax or WarpShuffleKind.ReduceAnd
            or WarpShuffleKind.ReduceOr or WarpShuffleKind.ReduceXor;

    /// <summary>
    /// Returns the unsupported-context fragment for <paramref name="invocation"/> when it stands in a context that the
    /// CPU state machine does not split, or <see langword="null"/> when it may split there.
    /// </summary>
    /// <remarks>
    /// <paramref name="isWarpShuffle"/>: a <c>Gpu.GridStrideLoop</c> body is allowed for a warp shuffle, which the CPU
    /// lowering turns into a loop (#1091). A <c>using</c> or <c>fixed</c> statement and the scope of a <c>using</c>
    /// declaration are reported for a warp shuffle only: the CPU lowering does not flatten them, so the shuffle would
    /// not split there (#1172); a call that suspends there is GSH0003 / GSH0043 (<c>BarrierDivergenceChecks</c>).
    /// </remarks>
    public static string? FindUnsupportedContext(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        bool isWarpShuffle)
    {
        SyntaxNode child = invocation;
        for (var ancestor = invocation.Parent;
             ancestor is not (null or MemberDeclarationSyntax);
             child = ancestor, ancestor = ancestor.Parent)
        {
            switch (ancestor)
            {
                // The transpiler inlines the body (CUDA), and the lowering inlines a SyncGrid body and turns ForUnroll,
                // and a GridStrideLoop whose body holds a warp shuffle, into a loop (CPU dialects, managed)
                case AnonymousFunctionExpressionSyntax when IsInlinedMacroBody(ancestor, semanticModel):
                    break;
                case AnonymousFunctionExpressionSyntax when isWarpShuffle &&
                    GpuMacroClassifier.ClassifyBody(ancestor, semanticModel).Kind == GpuMacroKind.GridStrideLoop:
                    break;
                case AnonymousFunctionExpressionSyntax:
                    return "in a lambda or anonymous method";
                case LocalFunctionStatementSyntax:
                    return "in a local function";
                case SwitchExpressionArmSyntax:
                    return "in a switch expression arm";
                case WhenClauseSyntax:
                    return "in a 'when' clause";
                case ConditionalAccessExpressionSyntax access when child == access.WhenNotNull:
                    return "after '?.'";
                case BinaryExpressionSyntax binary
                    when child == binary.Right && binary.IsKind(SyntaxKind.CoalesceExpression):
                    return "as the right operand of '??'";
                case UsingStatementSyntax usingStatement when isWarpShuffle && child == usingStatement.Statement:
                    return "in a using statement";
                case FixedStatementSyntax fixedStatement when isWarpShuffle && child == fixedStatement.Statement:
                    return "in a fixed statement";
                case BlockSyntax block when isWarpShuffle && IsInUsingDeclarationScope(block, invocation):
                    return "in the scope of a using declaration";
            }
        }

        return null;
    }

    /// <summary>
    /// The body lambda of a <c>Gpu.ForUnroll</c> or <c>Gpu.SyncGrid(body)</c>, however the call is spelled (#503).
    /// </summary>
    public static bool IsInlinedMacroBody(SyntaxNode lambda, SemanticModel semanticModel) =>
        GpuMacroClassifier.ClassifyBody(lambda, semanticModel).Kind is GpuMacroKind.ForUnroll or GpuMacroKind.SyncGrid;

    /// <summary>
    /// Whether <paramref name="barrier"/> in <paramref name="block"/> is in the scope of a <c>using</c> declaration of
    /// the block: one declared before it, or any one when the block is a nested block, whose statements the lowering
    /// moves into the enclosing block (<c>BranchRewriter</c>, <c>LoopRewriter</c>) so that its scope would end there.
    /// </summary>
    public static bool IsInUsingDeclarationScope(BlockSyntax block, InvocationExpressionSyntax barrier)
    {
        var isNested = block.Parent is BlockSyntax;
        return block.Statements
            .AsStackEnumerable()
            .Any((barrier.SpanStart, isNested), static (statement, state) =>
                UsingTranspiler.IsUsingDeclaration(statement) &&
                (state.isNested || statement.SpanStart < state.SpanStart));
    }
}
