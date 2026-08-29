using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.GpuKernel;

public static class GSH_01_StringTernary_InGpuKernel
{
    [GpuKernel]
    public static void StringTernaryKernel(int[] condition)
    {
        int tid = ThreadIndex.X;
        string result = condition[tid] > 0 ? "Hello" : "World";
    }
}
