#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for generic device functions whose type parameters appear in neither their signature nor their
/// body, as the fuzzer writes them: each is compiled once, as one function for every type argument, also one that
/// waits at a barrier, and one that passes its type parameter on to such a function.
/// </summary>
public static partial class GenericPhantomDifferentialKernels
{
    public static readonly string[] Columns = ["phantom_int", "phantom_byte", "phantom_sync", "phantom_forward"];

    [GpuDeviceFunction]
    public static void Phantom<T0>(int a, int b, ref int s) => s = (a * 3) + b;

    [GpuDeviceFunction]
    public static void PhantomSync<T0, T1>(int a, int b, out int s)
    {
        int kept = a - b;
        Gpu.SyncThreads();
        s = kept * 2;
    }

    // The type argument is passed on to a function that names it nowhere either
    [GpuDeviceFunction]
    public static int PhantomForward<T0, T1>(int a, int b)
    {
        int kept = a;
        GenericPhantomDifferentialKernels.Phantom<T0>(a, b, ref kept);
        return kept;
    }

    // Every thread reaches the barrier of PhantomSync; only the threads with a row write it
    [GpuKernel]
    public static void PhantomKernel(int[] xs, int[] r)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        int x = tid < xs.Length ? xs[tid] & 0xFFFF : 0;
        int s = 0;
        GenericPhantomDifferentialKernels.Phantom<int>(x, 1, ref s);
        int t = 0;
        GenericPhantomDifferentialKernels.Phantom<byte>(x, 2, ref t);
        GenericPhantomDifferentialKernels.PhantomSync<int, long>(x, 5, out int u);
        int v = GenericPhantomDifferentialKernels.PhantomForward<short, byte>(x, 4);
        if (tid < xs.Length)
        {
            r[tid * 4] = s;
            r[(tid * 4) + 1] = t;
            r[(tid * 4) + 2] = u;
            r[(tid * 4) + 3] = v;
        }
    }
}
