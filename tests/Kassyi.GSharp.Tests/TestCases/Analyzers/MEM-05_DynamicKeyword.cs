using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class MEM05_DynamicKeywordTestCase
{
    [GpuKernel]
    public static void Kernel()
    {
        // Error: Dynamic dispatch is strictly banned on GPU
        dynamic ctx = 42;
    }
}
