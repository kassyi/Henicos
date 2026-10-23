using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0003: syntax kinds that no transpiler rule handles. The analyzer finds them by running the transpiler, so a kind
// that a newer C# adds (the spread element of a collection expression, C# 12) is reported without a list of kinds.
public static partial class UnsupportedSyntaxKindKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void IndexFromEndKernel(int[] data, int[] output)
    {
        output[0] = data[^1];
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void TupleSwitchKernel(int[] a, int[] b, int[] output)
    {
        output[0] = (a[0], b[0]) switch
        {
            (0, 0) => 1,
            _ => 2
        };
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void TupleDeconstructionKernel((int X, int Y)[] pairs, int[] output)
    {
        int sum = 0;
        foreach (var (x, y) in pairs)
        {
            sum += x + y;
        }

        output[0] = sum;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void SpreadElementKernel(int[] data, int[] output)
    {
        int[] copy = [.. data];
        output[0] = copy[0];
    }
}
