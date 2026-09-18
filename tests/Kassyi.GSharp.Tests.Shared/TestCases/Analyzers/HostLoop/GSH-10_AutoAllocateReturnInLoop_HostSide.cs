using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Analyzers.HostLoop;

public class TestLoopAnalyzer
{
    [GpuKernel]
    public static float KernelTest()
    {
        return default;
    }

    public void HostMethod()
    {
        for (int i = 0; i < 10; i++)
        {
            var result = Launch_KernelTest();
        }
    }

    // Stub for the generated launch method so analyzer doesn't complain about undefined method
    public static float[] Launch_KernelTest() => new float[0];
}
