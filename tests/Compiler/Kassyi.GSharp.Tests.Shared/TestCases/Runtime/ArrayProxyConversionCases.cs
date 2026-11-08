using System;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.TestCases.Runtime;

// A raw pointer carries no length, so it must not become a kernel array argument without one (CS0029 on each line)
public static class ArrayProxyConversionCases
{
    public static GSharpArrayProxy FromPointer(IntPtr pointer)
    {
        GSharpArrayProxy proxy = pointer;
        return proxy;
    }

    public static GSharpArrayProxy FromZero() => IntPtr.Zero;
}
