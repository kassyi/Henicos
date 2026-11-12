namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>Represents a CUDA resource view descriptor (<c>CUDA_RESOURCE_VIEW_DESC</c>).</summary>
/// <remarks>
/// The struct has the size of <c>CUDA_RESOURCE_VIEW_DESC</c>, whose reserved tail of 64 bytes after
/// <see cref="LastLayer"/> the driver reads; it stays zero.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 112)]
public struct CudaResourceViewDescriptor
{
    /// <summary>Resource view format.</summary>
    public int Format;

    /// <summary>Width in elements.</summary>
    public nuint Width;

    /// <summary>Height in elements.</summary>
    public nuint Height;

    /// <summary>Depth in elements.</summary>
    public nuint Depth;

    /// <summary>First mipmap level.</summary>
    public uint FirstMipmapLevel;

    /// <summary>Last mipmap level.</summary>
    public uint LastMipmapLevel;

    /// <summary>First layer index.</summary>
    public uint FirstLayer;

    /// <summary>Last layer index.</summary>
    public uint LastLayer;
}
