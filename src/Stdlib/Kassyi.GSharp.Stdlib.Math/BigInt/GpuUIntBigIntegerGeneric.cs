using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Represents a fixed-size arbitrary-precision unsigned integer stored in value-type memory on the GPU.
/// </summary>
/// <typeparam name="TSize">The unmanaged struct type representing the limb storage layout.</typeparam>
[StructLayout(LayoutKind.Sequential)]
public struct GpuUIntBigInteger<TSize> where TSize : unmanaged
{
    /// <summary>
    /// The fixed-size limb storage backing this big integer instance.
    /// </summary>
    public TSize Limbs;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuUIntBigInteger{TSize}"/> struct with specified limbs.
    /// </summary>
    /// <param name="limbs">The limb storage payload.</param>
    public GpuUIntBigInteger(TSize limbs)
    {
        Limbs = limbs;
    }
}
