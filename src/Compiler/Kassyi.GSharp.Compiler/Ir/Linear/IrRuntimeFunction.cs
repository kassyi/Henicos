namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>A G# state-machine runtime helper called from <see cref="IrRuntimeCall"/>.</summary>
internal enum IrRuntimeFunction
{
    /// <summary>Publishes a lane's warp-operation inputs before suspending.</summary>
    WarpPublish,

    /// <summary>Reads the collective warp-operation result after resuming.</summary>
    WarpRead,

    /// <summary>Enters a warp reconvergence region (reserved for warp reconvergence).</summary>
    WarpRegionEnter,

    /// <summary>Leaves a warp reconvergence region (reserved for warp reconvergence).</summary>
    WarpRegionLeave,

    /// <summary>Marks a lane arriving at a reconvergence point (reserved for warp reconvergence).</summary>
    WarpReconvergeArrive,

    /// <summary>Passes through a reconvergence check (reserved for warp reconvergence).</summary>
    WarpReconvergePass
}
