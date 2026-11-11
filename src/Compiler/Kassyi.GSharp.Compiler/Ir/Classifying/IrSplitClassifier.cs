using Kassyi.GSharp.Compiler.Analysis;

namespace Kassyi.GSharp.Compiler.Ir.Classifying;

/// <summary>Decides whether a call of a method splits the state machine of the CPU paths, and at what.</summary>
/// <remarks>
/// A call splits when its callee is a device function that suspends (<see cref="SuspensionAnalysis"/>, which follows
/// the device functions it calls and the records of other assemblies), or a synchronization intrinsic that splits
/// (<see cref="SyncIntrinsicClassifier"/>). Both look at the symbol, never at how the call is spelled (#503). A device
/// function that suspends is a suspending call even when it is also an intrinsic, as the lowering has always taken it.
/// </remarks>
internal static class IrSplitClassifier
{
    /// <summary>Whether and how a call of <paramref name="method"/> splits the state machine.</summary>
    public static IrSplit Classify(IMethodSymbol method, Compilation compilation)
    {
        if (SuspensionAnalysis.IsSuspendingFunction(method, compilation))
        {
            return IrSplit.SuspendingCall;
        }

        var intrinsic = SyncIntrinsicClassifier.Classify(method);
        if (!intrinsic.SplitsStateMachine)
        {
            return IrSplit.None;
        }

        return intrinsic.Kind switch
        {
            SyncIntrinsicKind.BlockBarrier => IrSplit.BlockBarrier,
            SyncIntrinsicKind.GridBarrier => IrSplit.GridBarrier,
            SyncIntrinsicKind.WarpShuffle => IrSplit.WarpShuffle(intrinsic.Shuffle),
            _ => throw new InvalidOperationException(
                $"The intrinsic {method.Name} splits the state machine but its kind {intrinsic.Kind} has no split.")
        };
    }
}
