using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class MEM05_StackallocLoopTestCase
{
    [GpuKernel]
    public static void Kernel()
    {
        while (true)
        {
            unsafe
            {
                // Error: Recursive stackalloc in loop causes instant VRAM overflow
                int* p = stackalloc int[10];
            }
        }
    }
}
