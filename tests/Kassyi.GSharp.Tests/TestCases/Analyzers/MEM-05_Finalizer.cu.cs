using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class GpuContext
{
    private void Free() { }

    // Error: Finalizers are not supported on GPU. Use IDisposable.
    ~GpuContext()
    {
        Free();
    }
}

public static class MEM05_FinalizerTestCase
{
    [GpuKernel]
    public static void Kernel()
    {
        var ctx = new GpuContext();
    }
}
