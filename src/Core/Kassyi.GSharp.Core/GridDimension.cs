namespace Kassyi.GSharp.Core;

/// <summary>
/// Provides access to the dimensions of the grid (<c>gridDim</c>).
/// </summary>
// ReSharper disable once UnusedType.Global -- Public API exposed for GPU kernels
// ReSharper disable once UnusedMember.Global -- Public API exposed for GPU kernels
[CudaIntrinsicPattern("(int)gridDim.{0}")]
public static class GridDimension
{
    /// <summary>Gets the number of blocks per grid along the X axis (<c>gridDim.x</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int X => ManagedGpuContext.IsActive ? ManagedGpuContext.GridDimX : GpuOnlyException.Throw<int>();

    /// <summary>Gets the number of blocks per grid along the Y axis (<c>gridDim.y</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Y => ManagedGpuContext.IsActive ? ManagedGpuContext.GridDimY : GpuOnlyException.Throw<int>();

    /// <summary>Gets the number of blocks per grid along the Z axis (<c>gridDim.z</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Z => ManagedGpuContext.IsActive ? ManagedGpuContext.GridDimZ : GpuOnlyException.Throw<int>();
}
