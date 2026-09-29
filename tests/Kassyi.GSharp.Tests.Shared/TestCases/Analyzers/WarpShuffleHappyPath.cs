using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class WarpShuffleHappyPath
{
    [GpuKernel]
    public static void KernelMethod()
    {
        int val = Warp.ShuffleDownSync(0xFFFFFFFF, 1, 1);
        
        void LocalFunc()
        {
            int val2 = Warp.ShuffleDownSync(0xFFFFFFFF, 1, 1);
        }
    }

    [GpuDeviceFunction]
    public static void DeviceMethod()
    {
        int val = Warp.ShuffleDownSync(0xFFFFFFFF, 1, 1);
    }
}
