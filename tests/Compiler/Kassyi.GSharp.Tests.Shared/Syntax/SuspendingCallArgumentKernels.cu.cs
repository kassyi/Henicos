// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels that call a device function that suspends on the CPU (it shuffles across the warp) with omitted optional
/// arguments and with named arguments.
/// </summary>
public static partial class SuspendingCallArgumentKernels
{
    private const uint FullMask = 0xFFFFFFFFu;

    [GpuDeviceFunction]
    public static int PairSum(int value, uint scale = 3u, bool negate = false, PacketColor color = PacketColor.Blue)
    {
        int neighbor = Warp.ShuffleXorSync(FullMask, value, 1);
        int result = (value + neighbor) * (int)scale + (int)color;
        return negate ? -result : result;
    }

    [GpuKernel]
    public static void OmittedOptionalArgumentsKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = PairSum(input[tid]);
    }

    // Skips 'scale' and 'color' by naming 'negate'
    [GpuKernel]
    public static void NamedArgumentKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = PairSum(input[tid], negate: true);
    }

    [GpuKernel]
    public static void ReorderedNamedArgumentsKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = PairSum(color: PacketColor.Green, value: input[tid] + 1, scale: 2u);
    }
}
