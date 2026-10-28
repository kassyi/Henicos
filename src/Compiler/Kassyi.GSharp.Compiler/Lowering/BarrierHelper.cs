namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Provides helper methods to detect phase barrier and warp shuffle invocations in lowered syntax trees.</summary>
/// <remarks>
/// [WHY] The helpers read only the annotations that <c>AstLoweringPipeline</c> puts on the original tree from the symbol
/// of each call (<see cref="LoweringAnnotations.SyncIntrinsicAnnotationKind"/>,
/// <see cref="LoweringAnnotations.SuspendingCallAnnotationKind"/>), never the spelling of the call: a <c>using</c> alias,
/// <c>using static</c> or a full name splits the state machine like <c>Gpu.SyncThreads()</c>, and a user's own method of
/// the same name does not (#503). A lowering without a semantic model (the Tier 2 tests) annotates the calls itself.
/// </remarks>
internal static class BarrierHelper
{
    /// <summary>
    /// Determines whether the specified invocation represents a thread barrier, warp-level synchronization, or a call to a
    /// device function that reaches one (marked by <see cref="LoweringAnnotations.SuspendingCallAnnotationKind"/>).
    /// </summary>
    public static bool IsPhaseBarrier(InvocationExpressionSyntax inv) =>
        IsSuspendingCall(inv) || inv.HasAnnotations(LoweringAnnotations.SyncIntrinsicAnnotationKind);

    /// <summary>Determines whether the specified invocation is a block barrier (<c>Gpu.SyncThreads()</c>).</summary>
    public static bool IsBlockBarrier(InvocationExpressionSyntax inv) =>
        TryGetSyncIntrinsic(inv, out var data) && data == LoweringAnnotations.BlockBarrierAnnotationData;

    /// <summary>Determines whether the specified invocation is a grid barrier (<c>Gpu.SyncGrid()</c>).</summary>
    public static bool IsGridBarrier(InvocationExpressionSyntax inv) =>
        TryGetSyncIntrinsic(inv, out var data) && data == LoweringAnnotations.GridBarrierAnnotationData;

    /// <summary>Determines whether the specified invocation calls a device function that may suspend the calling thread.</summary>
    public static bool IsSuspendingCall(InvocationExpressionSyntax inv) =>
        inv.HasAnnotations(LoweringAnnotations.SuspendingCallAnnotationKind);

    /// <summary>Determines whether the specified invocation calls a device function that may suspend and returns a value.</summary>
    public static bool IsValueReturningSuspendingCall(InvocationExpressionSyntax inv)
    {
        foreach (var annotation in inv.GetAnnotations(LoweringAnnotations.SuspendingCallAnnotationKind))
        {
            if (annotation.Data == LoweringAnnotations.ValueReturningCallAnnotationData)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether the specified invocation is a warp shuffle (<c>Warp.ShuffleSync</c>, <c>ShuffleDownSync</c>,
    /// <c>ShuffleUpSync</c> or <c>ShuffleXorSync</c>).
    /// </summary>
    /// <remarks>
    /// Other <c>Warp</c> / <c>Gpu</c> members (<c>Gpu.WarpId()</c>, <c>Gpu.LaneId()</c>) are plain intrinsics and do not
    /// split the state machine.
    /// </remarks>
    public static bool IsWarpShuffle(InvocationExpressionSyntax inv) => TryGetWarpShuffleRead(inv, out _);

    /// <summary>
    /// Gets the Prelude function that reads the result of a warp shuffle after the state machine resumes
    /// (<c>__gsharp_warp_shuffle_read_down_sync</c> for <c>Warp.ShuffleDownSync</c>, ...).
    /// </summary>
    public static bool TryGetWarpShuffleRead(InvocationExpressionSyntax inv, [NotNullWhen(true)] out string? readFunction)
    {
        readFunction = TryGetSyncIntrinsic(inv, out var data)
            ? data switch
            {
                LoweringAnnotations.ShuffleSyncAnnotationData => GSharpInternalNames.Prelude.WarpShuffleReadSync,
                LoweringAnnotations.ShuffleDownAnnotationData => GSharpInternalNames.Prelude.WarpShuffleReadDownSync,
                LoweringAnnotations.ShuffleUpAnnotationData => GSharpInternalNames.Prelude.WarpShuffleReadUpSync,
                LoweringAnnotations.ShuffleXorAnnotationData => GSharpInternalNames.Prelude.WarpShuffleReadXorSync,
                _ => null
            }
            : null;
        return readFunction is not null;
    }

    // The data of the synchronization intrinsic annotation of a call
    private static bool TryGetSyncIntrinsic(InvocationExpressionSyntax inv, [NotNullWhen(true)] out string? data)
    {
        foreach (var annotation in inv.GetAnnotations(LoweringAnnotations.SyncIntrinsicAnnotationKind))
        {
            if (annotation.Data is { } annotationData)
            {
                data = annotationData;
                return true;
            }
        }

        data = null;
        return false;
    }

    /// <summary>Extracts the phase barrier invocation expression from a statement if present.</summary>
    public static InvocationExpressionSyntax? GetPhaseBarrierInvocation(StatementSyntax stmt)
    {
        switch (stmt)
        {
            case ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax { Right: InvocationExpressionSyntax rightInv }
            } when IsPhaseBarrier(rightInv):
                return rightInv;
            case ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv } when IsPhaseBarrier(inv):
                return inv;
            case LocalDeclarationStatementSyntax localDecl:
                {
                    var initializer = localDecl.Declaration.Variables.FirstOrDefault()?.Initializer;
                    if (initializer?.Value is InvocationExpressionSyntax inv && IsPhaseBarrier(inv))
                    {
                        return inv;
                    }

                    break;
                }
        }

        return null;
    }
}
