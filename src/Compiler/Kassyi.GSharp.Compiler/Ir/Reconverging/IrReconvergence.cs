using Kassyi.GSharp.Compiler.Ir.Splitting;

namespace Kassyi.GSharp.Compiler.Ir.Reconverging;

/// <summary>The entry of the pass that decides the reconvergence points of a split-lowered body.</summary>
internal static class IrReconvergence
{
    /// <summary>
    /// Decides the uniformity of every branch and loop, then inserts reconvergence regions when
    /// <paramref name="method"/> reaches <c>Warp.ActiveMask()</c>.
    /// </summary>
    public static IrReconvergedBody Apply(IrMethod method, IrSplitLoweredBody body, Compilation compilation,
        bool isDeviceFunction)
    {
        var reaches = SuspensionAnalysis.ReachesActiveMask(method.Symbol, compilation);
        var classified = IrUniformityClassifier.Classify(method, body, compilation, isDeviceFunction, reaches);
        return reaches
            ? IrRegionInserter.Insert(classified, compilation, isDeviceFunction)
            : new IrReconvergedBody(classified, 0);
    }
}
