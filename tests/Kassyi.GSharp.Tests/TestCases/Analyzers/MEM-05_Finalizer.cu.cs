using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class FinalizerGpuContext
{
    private void Free() { }

    // Error: Finalizers are not supported on GPU. Use IDisposable.
    [GpuKernel]
    ~FinalizerGpuContext()
    {
        Free();
    }
}
