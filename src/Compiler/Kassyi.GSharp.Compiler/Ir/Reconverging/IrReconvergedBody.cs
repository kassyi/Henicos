using Kassyi.GSharp.Compiler.Ir.Splitting;

namespace Kassyi.GSharp.Compiler.Ir.Reconverging;

/// <summary>A split-lowered body with condition uniformity decided and reconvergence regions inserted.</summary>
/// <param name="body">The body with uniformity classified and reconvergence regions inserted.</param>
/// <param name="regionCount">
/// The number of reconvergence regions inserted, which equals the reserved warp sites.
/// </param>
internal sealed class IrReconvergedBody(IrSplitLoweredBody body, int regionCount)
{
    /// <summary>The body with uniformity classified and reconvergence regions inserted.</summary>
    public IrSplitLoweredBody Body { get; } = body;

    /// <summary>The number of reconvergence regions inserted, which equals the reserved warp sites.</summary>
    public int RegionCount { get; } = regionCount;
}
