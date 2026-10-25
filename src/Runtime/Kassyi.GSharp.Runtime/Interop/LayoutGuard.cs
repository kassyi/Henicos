using System;

namespace Kassyi.GSharp.Runtime.Interop;

public static class LayoutGuard
{
    public static void ThrowSizeMismatch(string structName, int expectedSize, int actualSize)
    {
        throw new InvalidOperationException(
            $"Struct layout size mismatch for '{structName}': expected {expectedSize} bytes, but CLR Unsafe.SizeOf reported {actualSize} bytes.");
    }
}
