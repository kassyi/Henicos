using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// Intrinsics called from a lambda or local function nested in a GPU function run on the device.
public static class HostIntrinsicNestedHappyPath
{
    [GpuDeviceFunction]
    public static int DeviceMethodWithLambda()
    {
        Func<int> shuffle = () => Warp.ShuffleDownSync(0xFFFFFFFF, 1, 1);
        return shuffle();
    }

    [GpuKernel]
    public static void KernelWithLambdaInLocalFunction()
    {
        int LocalFunc()
        {
            Func<int> shuffle = () => Warp.ShuffleDownSync(0xFFFFFFFF, 1, 1);
            return shuffle();
        }

        LocalFunc();
    }
}
