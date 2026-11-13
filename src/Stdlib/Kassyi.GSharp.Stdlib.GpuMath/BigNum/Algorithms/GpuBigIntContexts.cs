namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Encapsulates device scratch buffers and intermediate memory arrays for multi-precision calculations.
/// </summary>
public struct BufferContext
{
    /// <summary>Primary scratch buffer 1.</summary>
    public uint[]? Buffer1;
    /// <summary>Primary scratch buffer 2.</summary>
    public uint[]? Buffer2;
    /// <summary>Primary scratch buffer 3.</summary>
    public uint[]? Buffer3;
    /// <summary>Temporary intermediate value buffer.</summary>
    public uint[]? ValueT;
    /// <summary>Quotient output buffer.</summary>
    public uint[]? Quotient;
    /// <summary>Remainder output buffer.</summary>
    public uint[]? Remainder;
    /// <summary>Remainder bitmask buffer.</summary>
    public uint[]? RemainderMask;
    /// <summary>General-purpose scratch buffer.</summary>
    public uint[]? TemporaryBuffer;
    /// <summary>Carry and borrow propagation accumulation buffer.</summary>
    public long[]? CarryBuffer;
}