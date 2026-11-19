using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// A cyclic struct (CS0523) as a local and as the return type of a device function
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class CyclicStructLocal
{
    public struct Cyc
    {
        public Cyc Inner;
        public int X;
    }

    [GpuDeviceFunction]
    public static Cyc Make(int[] output)
    {
        Cyc made = default;
        made.X = output[0];
        return made;
    }

    [GpuKernel]
    public static void LocalKernel(int[] output)
    {
        var value = Make(output);
        output[0] = value.X;
    }
}
