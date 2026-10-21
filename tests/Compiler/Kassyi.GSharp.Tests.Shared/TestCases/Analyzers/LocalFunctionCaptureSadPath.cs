using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class GSH03_LocalFunctionCaptureSadPathCase
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void LocalFunctionCapturesHeapObjectKernel(int[] data)
    {
        int outerValue = 100;

        static int LocalCapture()
        {
            return outerValue + 1; // CS8421: A static local function cannot contain a reference to 'outerValue'
        }

        data[0] = LocalCapture();
    }
}
