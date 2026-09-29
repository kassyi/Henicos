using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases;

public static class DefaultAndNamedArgumentsTestCase
{
    [GpuDeviceFunction]
    public static int ComputeStep(int input, int offset = 0, int scale = 1)
    {
        return (input + offset) * scale;
    }

    [GpuKernel]
    public static void SampleKernel(int[] data)
    {
        int tid = ThreadIndex.X;

        // 1. Standard call (offset = 0, scale = 1 omitted)
        int r1 = ComputeStep(data[tid]);

        // 2. Named arguments with reordering
        int r2 = ComputeStep(data[tid], scale: 2, offset: 5);

        // 3. Partial default arguments (offset omitted)
        int r3 = ComputeStep(data[tid], scale: 4);

        data[tid] = r1 + r2 + r3;
    }
}
