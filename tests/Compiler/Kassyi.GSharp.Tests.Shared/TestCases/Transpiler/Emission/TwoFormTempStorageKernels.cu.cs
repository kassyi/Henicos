#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Two-form CUDA kernels with multiple discard <c>out _</c> arguments and multiple lifted
/// <c>SharedMemory&lt;T&gt;</c> creations (Issue #2334). Both forms must number their temporaries
/// cleanly without reusing keys from the first form.
/// </summary>
public static partial class TwoFormTempStorageKernels
{
    [GpuDeviceFunction]
    public static void Get(out int value)
    {
        value = ThreadIndex.X;
    }

    [GpuKernel]
    public static void DiscardTwiceKernel(in int n, int[] data)
    {
        Get(out _);
        Get(out _);
        data[0] = n;
    }

    [GpuDeviceFunction]
    public static void Touch(SharedMemory<int> smem)
    {
        smem[0] = ThreadIndex.X;
    }

    [GpuKernel]
    public static void SharedLiftTwiceKernel(in int n, int[] data)
    {
        Touch(new SharedMemory<int>(4));
        Touch(new SharedMemory<int>(4));
        data[0] = n;
    }
}
