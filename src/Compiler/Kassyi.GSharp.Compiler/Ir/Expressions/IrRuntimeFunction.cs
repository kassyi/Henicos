namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A G# warp reconvergence runtime helper called from <see cref="IrRuntimeCall"/>.</summary>
internal enum IrRuntimeFunction
{
    /// <summary>Enters a warp reconvergence region.</summary>
    WarpRegionEnter,

    /// <summary>Leaves a warp reconvergence region.</summary>
    WarpRegionLeave,

    /// <summary>Marks a lane arriving at a reconvergence point.</summary>
    WarpReconvergeArrive
}
