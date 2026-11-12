namespace Kassyi.GSharp.Core;

/// <summary>
/// Provides access to the dimensions of the thread block (<c>blockDim</c>).
/// </summary>
// ReSharper disable once UnusedType.Global -- Public API exposed for GPU kernels
// ReSharper disable once UnusedMember.Global -- Public API exposed for GPU kernels
[CudaIntrinsicPattern("(int)blockDim.{0}")]
public static class BlockDimension
{
    /// <summary>Gets the number of threads per block along the X axis (<c>blockDim.x</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int X => ManagedGpuContext.IsActive ? ManagedGpuContext.BlockDimX : GpuOnlyException.Throw<int>();

    /// <summary>Gets the number of threads per block along the Y axis (<c>blockDim.y</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Y => ManagedGpuContext.IsActive ? ManagedGpuContext.BlockDimY : GpuOnlyException.Throw<int>();

    /// <summary>Gets the number of threads per block along the Z axis (<c>blockDim.z</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Z => ManagedGpuContext.IsActive ? ManagedGpuContext.BlockDimZ : GpuOnlyException.Throw<int>();
}
