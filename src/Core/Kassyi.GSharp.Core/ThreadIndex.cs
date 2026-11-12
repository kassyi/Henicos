namespace Kassyi.GSharp.Core;

/// <summary>
/// Provides access to the current GPU thread coordinates (<c>threadIdx</c>) within the thread block.
/// </summary>
// ReSharper disable once UnusedType.Global -- Public API exposed for GPU kernels
// ReSharper disable once UnusedMember.Global -- Public API exposed for GPU kernels
// threadIdx, blockIdx, blockDim and gridDim are unsigned int in CUDA; the cast gives them the C# type int, so that
// var and the arithmetic on them are signed as in C# (#521)
[CudaIntrinsicPattern("(int)threadIdx.{0}")]
public static class ThreadIndex
{
    /// <summary>Gets the thread index along the X axis (<c>threadIdx.x</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int X => ManagedGpuContext.IsActive ? ManagedGpuContext.ThreadIdxX : GpuOnlyException.Throw<int>();

    /// <summary>Gets the thread index along the Y axis (<c>threadIdx.y</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Y => ManagedGpuContext.IsActive ? ManagedGpuContext.ThreadIdxY : GpuOnlyException.Throw<int>();

    /// <summary>Gets the thread index along the Z axis (<c>threadIdx.z</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Z => ManagedGpuContext.IsActive ? ManagedGpuContext.ThreadIdxZ : GpuOnlyException.Throw<int>();
}
