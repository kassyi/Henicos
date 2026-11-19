using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// Structs that hold themselves three and four times (CS0523), as kernel parameters and as locals
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class WideCyclicStructs
{
    public struct Three
    {
        public Three F0;
        public Three F1;
        public Three F2;
        public int X;
    }

    public struct Four
    {
        public Four F0;
        public Four F1;
        public Four F2;
        public Four F3;
        public int X;
    }

    [GpuKernel]
    public static void ThreeKernel(Three value, int[] output)
    {
        Three copy = value;
        output[0] = copy.X;
    }

    [GpuKernel]
    public static void FourKernel(Four value, int[] output)
    {
        Four copy = value;
        output[0] = copy.X;
    }
}
