namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>Represents a 1D or 2D CUDA array descriptor.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CudaArrayDescriptor
{
    /// <summary>Width of the array.</summary>
    public nuint Width;

    /// <summary>Height of the array.</summary>
    public nuint Height;

    /// <summary>Array format code.</summary>
    public int Format;

    /// <summary>Number of channels per element.</summary>
    public uint NumChannels;
}
