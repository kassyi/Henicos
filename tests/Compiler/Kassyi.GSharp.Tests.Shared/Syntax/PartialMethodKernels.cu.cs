using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// The attributes are on the defining declarations, which have no body; the implementing declarations are transpiled
public static partial class PartialMethodKernels
{
    [GpuKernel]
    public static partial void PartialFillKernel(int[] data);

    public static partial void PartialFillKernel(int[] data)
    {
        int i = ThreadIndex.X;
        data[i] = SevenPlus(i);
    }

    [GpuDeviceFunction]
    public static partial int SevenPlus(int i);

    public static partial int SevenPlus(int i) => 7 + i;

    // The barrier is in the implementing declaration: the call suspends, as the call of a device function that is not
    // partial does
    [GpuKernel]
    public static void PartialBarrierKernel(int[] shared, int[] result)
    {
        int i = ThreadIndex.X;
        result[i] = AddAfterBarrier(shared, i);
    }

    [GpuDeviceFunction]
    public static partial int AddAfterBarrier(int[] shared, int i);

    public static partial int AddAfterBarrier(int[] shared, int i)
    {
        shared[i] = i + 1;
        Gpu.SyncThreads();
        return shared[(i + 1) % shared.Length];
    }

    // C# removes the call of a partial method without an implementing declaration, its arguments included, so Bump
    // never runs
    [GpuKernel]
    public static void UnimplementedPartialKernel(int[] data)
    {
        int i = ThreadIndex.X;
        NotImplemented(Bump(data, i));
        data[i] += 1;
    }

    [GpuDeviceFunction]
    static partial void NotImplemented(int value);

    [GpuDeviceFunction]
    public static int Bump(int[] data, int i)
    {
        data[i] += 100;
        return data[i];
    }

    // The removed call is also the body of an expression-bodied lambda, of a local function, and an incrementor of a
    // for loop: in each, C# removes the call and its arguments (#1424)
    [GpuKernel]
    public static void UnimplementedPartialInExpressionBodiesKernel(int[] data)
    {
        int i = ThreadIndex.X;
        void Forget(int value) => NotImplemented(Bump(data, i) + value);

        Gpu.ForUnroll(0, 2, k => NotImplemented(Bump(data, i)));
        Forget(3);
        for (int k = 0; k < 2; k++, NotImplemented(Bump(data, i)))
        {
            data[i] += 1;
        }

        data[i] += 10;
    }
}
