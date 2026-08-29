using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class MEM05_ComInteropPInvokeGCHandleTestCase
{
    [GpuKernel]
    public static void Kernel()
    {
        int ctx = 42;
        // Error: GCHandle is a CPU-GC concept
        GCHandle handle = GCHandle.Alloc(ctx);
    }
}
