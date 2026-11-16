using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Marshaling;

public static class TestKernels
{
    [GpuKernel]
    public static void MatchingKernel(in int first, ref int second)
    {
    }

    [GpuKernel]
    public static void NonMatchingKernel(int first, ref int second)
    {
    }

    [GpuKernel]
    public static void UnrelatedTypeInKernel(in int first, ref float second)
    {
    }
}
