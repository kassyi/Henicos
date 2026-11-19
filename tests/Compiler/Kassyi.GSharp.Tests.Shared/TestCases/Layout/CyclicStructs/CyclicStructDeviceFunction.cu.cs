using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// A cyclic struct (CS0523) as the parameter of a device function only
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class CyclicStructDeviceFunction
{
    public struct Cyc
    {
        public Cyc Inner;
        public int X;
    }

    [GpuDeviceFunction]
    public static int Read(Cyc value)
    {
        return value.X;
    }

    [GpuKernel]
    public static void DeviceFunctionKernel(int[] output)
    {
        output[0] = Read(default);
    }
}
