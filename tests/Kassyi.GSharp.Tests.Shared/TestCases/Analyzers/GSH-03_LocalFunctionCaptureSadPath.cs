using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class CustomReferenceObject
{
    public int Value;
}

public static class GSH03_LocalFunctionCaptureSadPathCase
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void LocalFunctionCapturesHeapObjectKernel(int[] data)
    {
        CustomReferenceObject obj = new CustomReferenceObject { Value = 100 };

        int LocalCapture()
        {
            return obj.Value + 1; // Capturing heap reference type
        }

        data[0] = LocalCapture();
    }
}
