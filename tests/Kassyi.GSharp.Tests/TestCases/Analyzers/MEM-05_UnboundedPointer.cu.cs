using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class MEM05_UnboundedPointerTestCase
{
    private static unsafe int* GetPointer() => null;

    [GpuKernel]
    public static void Kernel()
    {
        // Banned: Unbounded Pointer Operation outside unsafe block (CS0214) or untracked pointer
        int* ptr = GetPointer();
    }
}
