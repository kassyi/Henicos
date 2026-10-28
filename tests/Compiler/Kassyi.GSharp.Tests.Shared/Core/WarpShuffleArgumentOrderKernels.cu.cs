// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Warp shuffles whose arguments have side effects (#788): C# evaluates each argument once, left to right in the order
/// written, also when the arguments are named and written out of parameter order.
/// </summary>
public static partial class WarpShuffleArgumentOrderKernels
{
    // The value argument increments v once; the lane without a source (the last one) gets the value it published
    [GpuKernel]
    public static void ShuffleDownPostIncrementKernel(int[] result, int[] afterV)
    {
        int v = (ThreadIndex.X + 1) * 10;
        int r = Warp.ShuffleDownSync(0xFFFFFFFFu, v++, 1u);
        result[ThreadIndex.X] = r;
        afterV[ThreadIndex.X] = v;
    }

    // Each argument records the step at which it was evaluated
    [GpuKernel]
    public static void ShuffleNamedArgSideEffectOrderKernel(int[] result, int[] orderOut)
    {
        int step = 0;
        int vOrder = 0, wOrder = 0, mOrder = 0, lOrder = 0;
        int r = Warp.ShuffleSync(
            var: (vOrder = ++step) + 100,
            width: ((wOrder = ++step) * 0) + 8,
            mask: (uint)((mOrder = ++step) * 0) | 0xFFFFFFFFu,
            srcLane: (lOrder = ++step) * 0);
        result[ThreadIndex.X] = r;
        if (ThreadIndex.X == 0)
        {
            orderOut[0] = vOrder;
            orderOut[1] = wOrder;
            orderOut[2] = mOrder;
            orderOut[3] = lOrder;
            orderOut[4] = step;
        }
    }
}
