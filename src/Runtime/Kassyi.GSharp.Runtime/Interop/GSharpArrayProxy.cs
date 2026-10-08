using System;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// A managed proxy struct representing the memory layout of the native GSharpArray&lt;T&gt; struct in CUDA.
/// Used for marshalling span/array arguments to explicit layout AOT context structs.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GSharpArrayProxy
{
    [FieldOffset(0)]
    public IntPtr Data;

    [FieldOffset(8)]
    public int Length;
    
    // 4 bytes padding at offset 12 is implicitly managed by Size = 16

    /// <summary>
    /// Implicitly converts a raw device pointer to a proxy struct with unknown length (0).
    /// Used for backward compatibility with manual AOT marshaling.
    /// </summary>
    public static implicit operator GSharpArrayProxy(IntPtr ptr) => new GSharpArrayProxy { Data = ptr, Length = 0 };
}
