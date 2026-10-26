using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// Sum is not symmetric in a and b, so arguments that land in the wrong parameter show
public struct CtorScaled
{
    public int Sum;
    public int Scale;

    public CtorScaled(int a, int b = 10)
    {
        Sum = a * 10 + b;
        Scale = 2;
    }

    public CtorScaled(int a, int b, int scale) : this(a, b)
    {
        Scale = scale;
    }
}

public struct CtorWithInitializers
{
    public int BaseValue = 100;
    public int Bonus { get; set; } = 7;
    public int Result;

    public CtorWithInitializers(int x)
    {
        Result = BaseValue + Bonus + x;
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

    [GpuKernel]
    public static void ConstructorChainedThisKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = new CtorScaled(tid, 5, 9);
        output[tid] = s.Sum * 100 + s.Scale;
    }

    [GpuKernel]
    public static void ConstructorFieldInitializersKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = new CtorWithInitializers(tid);
        output[tid] = s.Result;
    }

    [GpuKernel]
    public static void ConstructorFieldInitializersDefaultKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s1 = new CtorWithInitializers();
        var s2 = new CtorWithInitializers { Result = tid + 10 };
        output[tid] = s1.BaseValue + s1.Bonus + s2.BaseValue + s2.Bonus + s2.Result;
    }
}
