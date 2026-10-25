// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// Reproductions of struct handling gaps. A kernel that breaks the build (internal compiler error or a C++ compile error)
// cannot carry a test-level Skip, so it sits behind a conditional symbol named in the #if above it. Define the symbol on
// top of this file (and of StructReproTests.cs) to see the failure.

public struct ReproPair<T> where T : unmanaged
{
    public T First;
    public T Second;
}

public struct ReproArrayWrap<T> where T : unmanaged
{
    public T[] Items;
}

public struct ReproRange
{
    public int Low;
    public int High;
}

public struct ReproSpan
{
    public int Low;
    public int High;
}

public struct ReproPt
{
    public int X;
    public int Y;
}

public struct ReproClamped
{
    private int _v;
    private int _scaled;

    public int Value
    {
        get => _v;
        init => _v = value < 0 ? 0 : value;
    }

    public int Scaled
    {
        get { return _scaled; }
        set { _scaled = value < 0 ? 0 : value * 2; }
    }
}

public static partial class StructReproKernels
{
    [GpuKernel]
    public static void GenericIntFirstKernel(int[] ints, float[] floats)
    {
        var a = new ReproPair<int> { First = 1, Second = 2 };
        var b = new ReproPair<float> { First = 1.5f, Second = 2.5f };
        ints[0] = a.First + a.Second;
        floats[0] = b.First + b.Second;
    }

    [GpuKernel]
    public static void GenericFloatFirstKernel(int[] ints, float[] floats)
    {
        var b = new ReproPair<float> { First = 1.5f, Second = 2.5f };
        var a = new ReproPair<int> { First = 1, Second = 2 };
        floats[0] = b.First + b.Second;
        ints[0] = a.First + a.Second;
    }

    [GpuKernel]
    public static void GenericNestedKernel(int[] ints, float[] floats)
    {
        var a = new ReproPair<ReproPair<int>>
        {
            First = new ReproPair<int> { First = 1, Second = 2 },
            Second = new ReproPair<int> { First = 3, Second = 4 }
        };
        var b = new ReproPair<ReproPair<float>>
        {
            First = new ReproPair<float> { First = 1.5f, Second = 2.5f },
            Second = new ReproPair<float> { First = 3.5f, Second = 4.5f }
        };
        ints[0] = a.First.First + a.First.Second + a.Second.First + a.Second.Second;
        floats[0] = b.First.First + b.First.Second + b.Second.First + b.Second.Second;
    }

    [GpuKernel]
    public static void GenericArrayFieldKernel(int[] ints, float[] floats)
    {
        var a = new ReproArrayWrap<int> { Items = ints };
        var b = new ReproArrayWrap<float> { Items = floats };
        a.Items[0] = a.Items[1] + a.Items[2];
        b.Items[0] = b.Items[1] + b.Items[2];
    }

    [GpuDeviceFunction]
    public static ReproRange MakeRange(int low, int high) => new ReproRange { Low = low, High = high };

    [GpuKernel]
    public static void ReturnOnlyStructKernel(int[] output)
    {
        output[0] = MakeRange(2, 7).High - MakeRange(2, 7).Low;
    }

    [GpuDeviceFunction]
    public static ReproSpan MakeSpan(int low, int high) => new ReproSpan { Low = low, High = high };

    [GpuKernel]
    public static void ReturnAndLocalStructKernel(int[] output)
    {
        ReproSpan s = MakeSpan(2, 7);
        output[0] = s.High - s.Low;
    }

    // Control: the same shape with a local that does not share a member name
    [GpuKernel]
    public static void BarrierLocalDifferentNameKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int W = tid;
        Gpu.SyncThreads();
        var p = new ReproPt { X = 1, Y = 2 };
        var q = p with { X = 3 };
        output[tid] = W * 100 + q.X * 10 + p.X;
    }

    [GpuKernel]
    public static void InitBodyKernel(int[] output)
    {
        var c = new ReproClamped { Value = -5 };
        var d = new ReproClamped { Value = 7 };
        output[0] = c.Value;
        output[1] = d.Value;
    }

    [GpuKernel]
    public static void SetAndWithAccessorBodyKernel(int[] output)
    {
        var c = new ReproClamped { Value = 10, Scaled = 3 };
        var d = c with { Value = -4, Scaled = -2 };
        var e = c with { Value = 8 };
        e.Scaled = 5;
        output[0] = c.Value * 100 + c.Scaled;
        output[1] = d.Value * 100 + d.Scaled;
        output[2] = e.Value * 100 + e.Scaled;
    }
}
