using Kassyi.GSharp.Core;
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests.Shared.Analyzers.HostLoop;

public class TestLoopAnalyzerDeconstruction
{
    [GpuKernel]
    public static float KernelTest()
    {
        return default;
    }

    public void HostMethod()
    {
        var list = new List<(int a, int b)> { (1, 2) };
        foreach (var (a, b) in list)
        {
            var result = Launch_KernelTest();
        }
    }

    public static float[] Launch_KernelTest() => new float[0];
}
