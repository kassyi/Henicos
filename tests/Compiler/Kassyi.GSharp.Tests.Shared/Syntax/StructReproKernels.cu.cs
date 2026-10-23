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

    public int Value
    {
        get => _v;
        init => _v = value < 0 ? 0 : value;
    }
}

public static partial class StructReproKernels
{
    // Reproduces #407: the second instantiation reuses the layout of the first (floats[0] is 3, not 4)
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

// Reproduces #480: defining the symbol makes the C++ build fail (the struct is undefined where the device function returns it)
#if REPRO_A47_RETURN_ONLY
    // C++: the struct definition is emitted after the device function that returns it (MSVC C4430, C2086, C2146, ...)
    [GpuDeviceFunction]
    public static ReproRange MakeRange(int low, int high) => new ReproRange { Low = low, High = high };

    [GpuKernel]
    public static void ReturnOnlyStructKernel(int[] output)
    {
        output[0] = MakeRange(1, 5).High;
    }
#endif

// Reproduces #480: defining the symbol makes the C++ build fail even when the kernel also uses the struct as a local
#if REPRO_A47_RETURN_AND_LOCAL
    // Writing the struct as a local type in the kernel does not help
    [GpuDeviceFunction]
    public static ReproSpan MakeSpan(int low, int high) => new ReproSpan { Low = low, High = high };

    [GpuKernel]
    public static void ReturnAndLocalStructKernel(int[] output)
    {
        ReproSpan s = MakeSpan(1, 5);
        output[0] = s.High;
    }
#endif

// Reproduces #481: defining the symbol makes the build fail with GSH0048 (InvalidCastException in ContextHoister)
#if REPRO_A48_INIT
    // ICE: InvalidCastException in ContextHoister.VisitLocalDeclarationStatement
    [GpuKernel]
    public static void BarrierLocalSameNameInitializerKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int X = tid;
        Gpu.SyncThreads();
        var p = new ReproPt { X = 1, Y = 2 };
        output[tid] = X * 100 + p.X;
    }
#endif

// Reproduces #481: defining the symbol makes the build fail with GSH0048 (InvalidCastException in ContextHoister)
#if REPRO_A48_WITH
    // ICE: same InvalidCastException
    [GpuKernel]
    public static void BarrierLocalSameNameWithKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int X = tid;
        Gpu.SyncThreads();
        var p = new ReproPt();
        var q = p with { X = 3 };
        output[tid] = X * 100 + q.X;
    }
#endif

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

// Reproduces #482: defining the symbol makes the C++ build fail (no member for the property, init body not emitted)
#if REPRO_INIT_BODY
    // C++: the struct has no member 'Value' (MSVC C2039 on __gsharp_mb_Value and __this); the init body is not emitted
    [GpuKernel]
    public static void InitBodyKernel(int[] output)
    {
        var c = new ReproClamped { Value = -5 };
        var d = new ReproClamped { Value = 7 };
        output[0] = c.Value;
        output[1] = d.Value;
    }
#endif
}
