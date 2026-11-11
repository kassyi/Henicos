namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>Represents a CUDA texture descriptor (<c>CUDA_TEXTURE_DESC</c>).</summary>
/// <remarks>
/// The struct has the size of <c>CUDA_TEXTURE_DESC</c>, whose reserved tail of 48 bytes after
/// <see cref="BorderColor3"/> the driver reads; it stays zero.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 104)]
public struct CudaTextureDescriptor
{
    /// <summary>Address mode 0.</summary>
    public int AddressMode0;

    /// <summary>Address mode 1.</summary>
    public int AddressMode1;

    /// <summary>Address mode 2.</summary>
    public int AddressMode2;

    /// <summary>Filter mode.</summary>
    public int FilterMode;

    /// <summary>Flags.</summary>
    public uint Flags;

    /// <summary>Max anisotropy.</summary>
    public uint MaxAnisotropy;

    /// <summary>Mipmap filter mode.</summary>
    public int MipmapFilterMode;

    /// <summary>Mipmap level bias.</summary>
    public float MipmapLevelBias;

    /// <summary>Min mipmap level clamp.</summary>
    public float MinMipmapLevelClamp;

    /// <summary>Max mipmap level clamp.</summary>
    public float MaxMipmapLevelClamp;

    /// <summary>Border color 0.</summary>
    public float BorderColor0;

    /// <summary>Border color 1.</summary>
    public float BorderColor1;

    /// <summary>Border color 2.</summary>
    public float BorderColor2;

    /// <summary>Border color 3.</summary>
    public float BorderColor3;
}
