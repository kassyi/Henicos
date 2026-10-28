using System;

namespace Kassyi.GSharp.Runtime.Interop;

public static class LayoutGuard
{
    public static void ThrowSizeMismatch(string structName, int expectedSize, int actualSize)
    {
        throw new InvalidOperationException(
            $"Struct layout size mismatch for '{structName}': expected {expectedSize} bytes, but CLR Unsafe.SizeOf reported {actualSize} bytes.");
    }

    /// <summary>
    /// Throws when the host mirror of a struct (a generated copy that names the hidden fields of its captured primary
    /// constructor parameters) puts a field at another offset than the CLR layout of the struct.
    /// </summary>
    public static void ThrowFieldOffsetMismatch(string structName)
    {
        throw new InvalidOperationException(
            $"Struct layout field offset mismatch for '{structName}': the generated host mirror does not have the CLR layout of the struct.");
    }

    /// <summary>
    /// Throws when a field of a struct that the launcher copies as one block is not at the offset that G# predicted for
    /// it, for example a struct of another assembly whose <c>[StructLayout]</c> or <c>[FieldOffset]</c> G# cannot read.
    /// </summary>
    public static void ThrowFieldOffsetMismatch(string structName, string fieldName, int expectedOffset, long actualOffset)
    {
        throw new InvalidOperationException(
            $"Struct layout field offset mismatch for '{structName}': field '{fieldName}' is expected at offset {expectedOffset}, but the CLR puts it at offset {actualOffset}.");
    }
}
