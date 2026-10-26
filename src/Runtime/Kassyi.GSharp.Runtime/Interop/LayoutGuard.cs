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
}
