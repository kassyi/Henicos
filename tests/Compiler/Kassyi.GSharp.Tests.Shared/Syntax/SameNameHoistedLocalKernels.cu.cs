// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels that declare locals of the same name in sibling scopes, where the locals live across a barrier or a warp
/// shuffle and therefore move into the state machine context.
/// </summary>
public static partial class SameNameHoistedLocalKernels
{
    private const uint FullMask = 0xFFFFFFFFu;

    // 'round' and 'value' are declared in both loops, with different types
    [GpuKernel]
    public static void SiblingLoopBarrierKernel(int[] data, uint[] unsignedData)
    {
        int tid = ThreadIndex.X;
        for (int round = 0; round < 2; round++)
        {
            int value = data[tid] + round;
            Gpu.SyncThreads();
            data[tid] = value;
        }

        for (uint round = 0u; round < 3u; round++)
        {
            uint value = unsignedData[tid] * 2u + round;
            Gpu.SyncThreads();
            unsignedData[tid] = value;
        }
    }

    // Only the first 'value' lives across a barrier
    [GpuKernel]
    public static void PartlyHoistedNameKernel(int[] data, uint[] unsignedData)
    {
        int tid = ThreadIndex.X;
        {
            int value = data[tid] * 3;
            Gpu.SyncThreads();
            data[tid] = value;
        }

        {
            uint value = unsignedData[tid] + 5u;
            unsignedData[tid] = value;
        }
    }

    // The first loop has no barrier and stays a for statement; its 'offset' shares the name of the hoisted one
    [GpuKernel]
    public static void PlainLoopBesideBarrierLoopKernel(int[] data, int n)
    {
        int tid = ThreadIndex.X;
        int total = 0;
        for (int offset = 3; offset < n; offset += 16)
        {
            total += offset;
        }

        for (int offset = 0; offset < 2; offset++)
        {
            int value = data[tid] + total + offset;
            Gpu.SyncThreads();
            data[tid] = value;
        }
    }

    // 'd' and 'neighbor' are declared in both loops; the shuffles make this device function suspend on the CPU
    [GpuDeviceFunction]
    public static int WarpPrefixAndTotal(int value)
    {
        int lane = ThreadIndex.X & 31;
        int original = value;
        for (int d = 1; d < 32; d <<= 1)
        {
            int neighbor = Warp.ShuffleUpSync(FullMask, value, (uint)d);
            if (lane >= d)
            {
                value += neighbor;
            }
        }

        int total = original;
        for (uint d = 16u; d >= 1u; d >>= 1)
        {
            int neighbor = Warp.ShuffleXorSync(FullMask, total, (int)d);
            total += neighbor;
        }

        return value * 1000 + total;
    }

    [GpuKernel]
    public static void WarpPrefixAndTotalKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = WarpPrefixAndTotal(input[tid]);
    }
}
