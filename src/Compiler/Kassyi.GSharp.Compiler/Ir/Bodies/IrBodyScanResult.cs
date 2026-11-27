namespace Kassyi.GSharp.Compiler.Ir.Bodies;

/// <summary>
/// What the scan of a body found, independent of any dialect or split policy: Stryker switches, split calls in
/// pre-order, the first opaque node, removed calls, the first macro, and shared-memory locals.
/// </summary>
internal sealed class IrBodyScanResult(bool hasStrykerMutant, ImmutableArray<IrSplitKind> splitCalls,
    string firstOpaqueDetail, bool hasRemovedCall, string firstMacroDetail, bool hasSharedMemoryLocal)
{
    // The scan reasons in the order they are checked (the order of IrBodyIneligibility)
    private static readonly IrBodyIneligibility[] s_order =
    [
        IrBodyIneligibility.StrykerMutant,
        IrBodyIneligibility.SplitCall,
        IrBodyIneligibility.OpaqueNode,
        IrBodyIneligibility.RemovedCall,
        IrBodyIneligibility.Macro,
        IrBodyIneligibility.SharedMemoryLocal
    ];

    /// <summary>Whether the body calls a Stryker mutant switch method.</summary>
    public bool HasStrykerMutant { get; } = hasStrykerMutant;

    /// <summary>The split kinds of the non-removed split calls encountered in pre-order.</summary>
    public ImmutableArray<IrSplitKind> SplitCalls { get; } = splitCalls;

    /// <summary>The detail of the first opaque node encountered; empty when none was found.</summary>
    public string FirstOpaqueDetail { get; } = firstOpaqueDetail;

    /// <summary>Whether the body contains a call that C# removes.</summary>
    public bool HasRemovedCall { get; } = hasRemovedCall;

    /// <summary>The kind name of the first <c>Gpu</c> macro encountered; empty when none was found.</summary>
    public string FirstMacroDetail { get; } = firstMacroDetail;

    /// <summary>Whether the body declares a local of a shared memory type.</summary>
    public bool HasSharedMemoryLocal { get; } = hasSharedMemoryLocal;

    /// <summary>
    /// The first reason in table order that applies under <paramref name="splits"/> and its detail, or
    /// <see cref="IrBodyIneligibility.None"/> with an empty detail when the body passes the scan.
    /// </summary>
    public (IrBodyIneligibility Reason, string Detail) Reason(IrSplitPolicy splits)
    {
        foreach (var candidate in s_order)
        {
            if (TryMatch(candidate, splits, out var detail))
            {
                return (candidate, detail);
            }
        }

        return (IrBodyIneligibility.None, "");
    }

    private bool TryMatch(IrBodyIneligibility candidate, IrSplitPolicy splits, out string detail)
    {
        switch (candidate)
        {
            case IrBodyIneligibility.StrykerMutant when HasStrykerMutant:
                detail = "";
                return true;
            case IrBodyIneligibility.SplitCall when splits == IrSplitPolicy.ExcludeEverySplit && !SplitCalls.IsEmpty:
                detail = SplitCalls[0].ToString();
                return true;
            case IrBodyIneligibility.OpaqueNode when FirstOpaqueDetail.Length > 0:
                detail = FirstOpaqueDetail;
                return true;
            case IrBodyIneligibility.RemovedCall when HasRemovedCall:
                detail = "";
                return true;
            case IrBodyIneligibility.Macro when FirstMacroDetail.Length > 0:
                detail = FirstMacroDetail;
                return true;
            case IrBodyIneligibility.SharedMemoryLocal when HasSharedMemoryLocal:
                detail = "";
                return true;
            default:
                detail = "";
                return false;
        }
    }
}
