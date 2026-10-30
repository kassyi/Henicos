using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics.SwitchCaseValues
{
    // GSH0003 for a case value that names no C++ case label: a float constant
    public static partial class SwitchCaseValueKernels
    {
        [GpuKernel]
        [SkipSnapshotConsistency("Sad Path")]
        public static void FloatCaseKernel(float[] data, int[] output)
        {
            int t = ThreadIndex.X;
            switch (data[t])
            {
                case 1.5f:
                    output[t] = 1;
                    break;
                default:
                    output[t] = 2;
                    break;
            }
        }
    }
}
