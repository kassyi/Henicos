using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0002 where the value of nameof stays a string on the device; none where its length or a comparison is used
public static partial class NameofStringKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NameofStringKernel(int[] r)
    {
        int c = nameof(NameofStringKernels)[0];
        r[0] = c;
        r[1] = nameof(r).Length + (nameof(r) == "r" ? 1 : 0);
    }

    private const string Missing = null;

    // The Length of a null constant string throws in C#, known at compile time: GSH0003, not nullptr.length
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NullConstantLengthKernel(int[] r)
    {
        r[0] = Missing.Length;
    }
}
