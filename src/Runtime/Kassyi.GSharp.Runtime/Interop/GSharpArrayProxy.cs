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

    [FieldOffset(0)]
    public IntPtr Data;

    [FieldOffset(8)]
    public int Length;

    /// <summary>
    /// Reserved: the 4 bytes after <see cref="Length"/> (<c>GSharpArray::flags</c> on the device), which the launchers
    /// write as 0 and the device does not read.
    /// </summary>
    [FieldOffset(12)]
    public int Flags;

    /// <summary>
    /// Initializes a proxy for <paramref name="length"/> elements at <paramref name="data"/>, with <see cref="Flags"/> 0.
    /// </summary>
    /// <param name="data">The address of the first element, or <see cref="IntPtr.Zero"/> for a null array.</param>
    /// <param name="length">The number of elements the kernel sees as the array's <c>Length</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="data"/> is null and <paramref name="length"/> is not 0.</exception>
    public GSharpArrayProxy(IntPtr data, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (data == IntPtr.Zero && length != 0)
        {
            throw new ArgumentException("A null array has no elements, so its length must be 0.", nameof(length));
        }

        Data = data;
        Length = length;
        Flags = 0;
    }
}
