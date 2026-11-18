#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

public static partial class UnsupportedIntrinsicArgumentKernels
{
    // The argument of an intrinsic that the CPU dialect does not implement is evaluated before the call fails
    [GpuKernel]
    public static void SideEffectKernel(int[] data)
    {
        var bar = new SharedMemory<ulong>(1);
        Gpu.MBarrierInit(ref bar[0], (uint)(data[1]++ + 100));
        data[2] = 7;
    }

    // Left to right: the first argument writes slot 1, the second slot 2
    [GpuKernel]
    public static void OrderKernel(int[] data)
    {
        var bar = new SharedMemory<ulong>(8);
        Gpu.MBarrierInit(ref bar[Tick(data, 1)], (uint)Tick(data, 2));
        data[3] = 7;
    }

    // A call with a value: the arguments are evaluated, then it fails, so the store never happens
    [GpuKernel]
    public static void ValueKernel(int[] data)
    {
        data[3] = Gpu.ConvertToSharedMemory(ref data[Tick(data, 1)]) == 0 ? 1 : 2;
    }

    // WMMA (ref + ref readonly without in): arguments evaluate left to right and emit &acc before failing
    [GpuKernel]
    public static void WmmaStoreKernel(int[] data)
    {
        WmmaAccumulator<WmmaM16N16K16, int> acc = default;
        Wmma.Store(ref data[Tick(data, 1)], acc, (uint)Tick(data, 2), WmmaMemoryLayout.RowMajor);
        data[3] = 7;
    }

    // An argument that throws: C# throws DivideByZeroException while it evaluates the arguments, before the call
    [GpuKernel]
    public static void ThrowingArgumentKernel(int[] data)
    {
        var bar = new SharedMemory<ulong>(1);
        Gpu.MBarrierInit(ref bar[0], (uint)(100 / data[0]));
        data[3] = 7;
    }

    [GpuDeviceFunction]
    private static int Tick(int[] data, int slot)
    {
        data[0]++;
        data[slot] = data[0];
        return 0;
    }
}
