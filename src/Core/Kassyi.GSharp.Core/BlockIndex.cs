namespace Kassyi.GSharp.Core;

/// <summary>
/// Provides access to the current thread block coordinates (<c>blockIdx</c>) within the grid.
/// </summary>
[CudaIntrinsicPattern("(int)blockIdx.{0}")]
public static class BlockIndex
{
    /// <summary>Gets the block index along the X axis (<c>blockIdx.x</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int X => ManagedGpuContext.IsActive ? ManagedGpuContext.BlockIdxX : GpuOnlyException.Throw<int>();

    /// <summary>Gets the block index along the Y axis (<c>blockIdx.y</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Y => ManagedGpuContext.IsActive ? ManagedGpuContext.BlockIdxY : GpuOnlyException.Throw<int>();

    /// <summary>Gets the block index along the Z axis (<c>blockIdx.z</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Z => ManagedGpuContext.IsActive ? ManagedGpuContext.BlockIdxZ : GpuOnlyException.Throw<int>();
}
