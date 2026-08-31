using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.GpuKernel;

public static class HostPropertyAccessKernel
{
    public static int HostProperty => Environment.ProcessorCount;

    [GpuKernel]
    public static void Kernel(int[] data)
    {
        data[0] = HostProperty;
    }
}
