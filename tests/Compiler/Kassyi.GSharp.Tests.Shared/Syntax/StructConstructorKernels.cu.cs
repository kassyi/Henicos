using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct CtorScaled
{
    public int Sum;
    public int Scale;

    public CtorScaled(int a, int b = 10)
    {
        Sum = a + b;
        Scale = 2;
    }
}

public static partial class StructConstructorKernels
{
    [GpuKernel]
    public static void ConstructorBodyKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = new CtorScaled(tid, 5);
        output[tid] = s.Sum * 100 + s.Scale;
    }

    [GpuKernel]
    public static void ConstructorDefaultArgumentKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = new CtorScaled(tid);
        output[tid] = s.Sum;
    }

    [GpuKernel]
    public static void ConstructorNamedArgumentKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = new CtorScaled(b: 1, a: tid);
        output[tid] = s.Sum;
    }
}
