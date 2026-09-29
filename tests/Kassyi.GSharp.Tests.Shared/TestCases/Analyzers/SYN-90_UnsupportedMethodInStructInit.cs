using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public struct TestStructSYN90
{
    public int A;
    public int B;
}

public static partial class SYN90_UnsupportedMethodInStructInitCase
{
    private static int UnsupportedMethod() => 42;

    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithUnsupportedMethodInStructInit(int[] data)
    {
        // Sad Path: Struct initializer invoking unsupported method
        var s = new TestStructSYN90 { A = UnsupportedMethod(), B = 2 };
        data[0] = s.A + s.B;
    }
}
