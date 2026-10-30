using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Warp shuffles in blocks whose last warp is partial, and the width and source lane rules of CUDA (#666, #692). Each
/// thread starts from <c>ThreadIndex.X + 1</c>, so a result names the lane it was read from.
/// </summary>
public static partial class PartialWarpShuffleKernels
{
    [GpuKernel]
    public static void ShuffleDownKernel(int[] result, uint delta)
    {
        int v = ThreadIndex.X + 1;
        v = Warp.ShuffleDownSync(0xFFFFFFFFu, v, delta);
        result[ThreadIndex.X] = v;
    }

    [GpuKernel]
    public static void ShuffleXorKernel(int[] result, int laneMask)
    {
        int v = ThreadIndex.X + 1;
        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, laneMask);
        result[ThreadIndex.X] = v;
    }

    [GpuKernel]
    public static void ShuffleIdxKernel(int[] result, int srcLane, int width)
    {
        int v = ThreadIndex.X + 1;
        v = Warp.ShuffleSync(0xFFFFFFFFu, v, srcLane, width);
        result[ThreadIndex.X] = v;
    }

    [GpuKernel]
    public static void ShuffleDownWidthKernel(int[] result, int width)
    {
        int v = ThreadIndex.X + 1;
        v = Warp.ShuffleDownSync(0xFFFFFFFFu, v, 1u, width);
        result[ThreadIndex.X] = v;
    }

    [GpuKernel]
    public static void ShuffleXorWidthKernel(int[] result, int laneMask, int width)
    {
        int v = ThreadIndex.X + 1;
        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, laneMask, width);
        result[ThreadIndex.X] = v;
    }

    [GpuKernel]
    public static void ShuffleUpWidthKernel(int[] result, uint delta, int width)
    {
        int v = ThreadIndex.X + 1;
        v = Warp.ShuffleUpSync(0xFFFFFFFFu, v, delta, width);
        result[ThreadIndex.X] = v;
    }

    // Named arguments in another order than the parameters: the read functions take them by position
    [GpuKernel]
    public static void ShuffleNamedArgumentsKernel(int[] result)
    {
        int v = ThreadIndex.X + 1;
        v = Warp.ShuffleSync(0xFFFFFFFFu, v, width: 8, srcLane: 3);
        result[ThreadIndex.X] = v;
    }

    // The value itself passed by name, first
    [GpuKernel]
    public static void ShuffleNamedValueKernel(int[] result)
    {
        int v = ThreadIndex.X + 1;
        v = Warp.ShuffleSync(var: v, width: 8, mask: 0xFFFFFFFFu, srcLane: 3);
        result[ThreadIndex.X] = v;
    }

    [GpuKernel]
    public static void ActiveMaskKernel(uint[] result)
    {
        result[ThreadIndex.X] = Warp.ActiveMask();
    }

    // Warp.ActiveMask() in a device function that does not suspend, which the managed path runs as the user's method
    [GpuKernel]
    public static void ActiveMaskViaDeviceKernel(uint[] result)
    {
        result[ThreadIndex.X] = MaskViaDevice();
    }

    [GpuDeviceFunction]
    public static uint MaskViaDevice() => Warp.ActiveMask();

    // A 2-D block: the lanes of a warp follow the linear thread index (x first, then y)
    [GpuKernel]
    public static void ActiveMask2DKernel(uint[] result)
    {
        result[ThreadIndex.Y * BlockDimension.X + ThreadIndex.X] = Warp.ActiveMask();
    }
}
