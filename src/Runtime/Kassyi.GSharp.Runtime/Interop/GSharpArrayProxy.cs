namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// A managed proxy struct representing the memory layout of the native GSharpArray&lt;T&gt; struct in CUDA.
/// Used for marshalling span/array arguments to explicit layout AOT context structs.
/// </summary>
/// <remarks>
/// A null array is <c>Data == 0</c>; an empty array has a non-null <see cref="Data"/> that is never dereferenced
/// (<see cref="EmptyArrayData"/> on CUDA, the pinned address of the empty array on the CPU), so the device tells the two
/// apart as C# does (#706).
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GSharpArrayProxy
{
    /// <summary>
    /// The <see cref="Data"/> of an empty array on CUDA: a non-null address that is never dereferenced, aligned for any
    /// element type, which no allocation returns (<c>cuMemAlloc</c> rejects zero bytes).
    /// </summary>
    public const nint EmptyArrayData = 256;

    /// <summary>
    /// The <see cref="Flags"/> bit that the launchers set for a null optional kernel argument (<c>T[]?</c>): the device
    /// reads it as an empty array instead of failing as a null array read does.
    /// </summary>
    public const int NullReadsAsEmpty = 1;

    [FieldOffset(0)]
    public IntPtr Data;

    [FieldOffset(8)]
    public int Length;

    /// <summary>
    /// Bit flags in the 4 bytes after <see cref="Length"/> (<c>GSharpArray::flags</c> on the device):
    /// <see cref="NullReadsAsEmpty"/>.
    /// </summary>
    [FieldOffset(12)]
    public int Flags;

    /// <summary>
    /// Implicitly converts a raw device pointer to a proxy struct with unknown length (0).
    /// Used for backward compatibility with manual AOT marshaling.
    /// </summary>
    public static implicit operator GSharpArrayProxy(IntPtr ptr) => new() { Data = ptr, Length = 0 };
}
