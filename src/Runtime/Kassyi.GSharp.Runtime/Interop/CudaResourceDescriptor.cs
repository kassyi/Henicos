namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>Represents a CUDA resource descriptor (<c>CUDA_RESOURCE_DESC</c>).</summary>
/// <remarks>
/// The struct has the size and field offsets of <c>CUDA_RESOURCE_DESC</c>: the driver reads the whole 128-byte
/// <c>res</c> union and <c>flags</c> behind it. The bytes of the union after <see cref="Res"/> stay zero.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 144)]
public struct CudaResourceDescriptor
{
    /// <summary>Resource type.</summary>
    [FieldOffset(0)]
    public int ResType;

    /// <summary>Resource handle or pointer: the first member of the <c>res</c> union.</summary>
    [FieldOffset(8)]
    public IntPtr Res;

    /// <summary>Flags (must be zero).</summary>
    [FieldOffset(136)]
    public uint Flags;
}
