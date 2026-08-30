using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class MEM05_MallocWarningTestCase
{
    [GpuKernel]
    public static void Kernel(int sz)
    {
        unsafe
        {
            // Warning: GSW010 "Manual unmanaged memory allocation detected. Ensure corresponding Free is called."
            void* p = Gpu.Unsafe.Malloc(sz);
        }
    }
}
