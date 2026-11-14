using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0117: a SharedMemory<T> whose count is a negative C# constant, as C# rejects `new int[-1]` (CS0248). A count of 0
// is an empty declaration, and a count known only at run time is checked when the kernel runs
public static partial class SharedMemoryNegativeSizeKernels
{
    private const int NegativeCount = -4;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NegativeLiteralKernel(int[] output)
    {
        var s = new SharedMemory<int>(-1);
        output[0] = 7;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NegativeConstantFormsKernel(int[] output)
    {
        SharedMemory<float> a = new(NegativeCount);
        var b = new SharedMemory<long>(2 - 3 * 4);
        output[0] = 7;
    }

    [GpuDeviceFunction]
    public static int NegativeInDeviceFunction(int i)
    {
        var s = new SharedMemory<int>(int.MinValue);
        return i;
    }

    // Not reported: a count of 0, a count known at run time and the smallest positive count
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void AcceptedCountsKernel(int[] output, int n)
    {
        var zero = new SharedMemory<int>(0);
        var dynamic = new SharedMemory<int>(n - 10);
        var one = new SharedMemory<int>(1);
        output[0] = NegativeInDeviceFunction(n);
    }
}
