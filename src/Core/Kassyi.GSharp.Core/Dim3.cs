namespace Kassyi.GSharp.Core;

/// <summary>
/// Represents a 3-dimensional coordinate vector used for grid and block dimensions in GPU kernels.
/// </summary>
/// <remarks>
/// This struct is a marker intercepted by the GSharp Transpiler and mapped to CUDA <c>dim3</c>.
/// </remarks>
public struct Dim3
{
    /// <summary>Gets the X component of the 3D vector.</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public int X => GpuOnlyException.Throw<int>();

    /// <summary>Gets the Y component of the 3D vector.</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public int Y => GpuOnlyException.Throw<int>();

    /// <summary>Gets the Z component of the 3D vector.</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public int Z => GpuOnlyException.Throw<int>();
}
