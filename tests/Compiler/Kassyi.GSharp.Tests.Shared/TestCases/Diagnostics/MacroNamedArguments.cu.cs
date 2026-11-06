using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// Gpu.ForUnroll and Gpu.GridStrideLoop with named arguments in another order than the parameters: the analyzer checks
// the argument that each parameter receives. The first kernels report; the last records what stays legal.
public static partial class MacroNamedArgumentKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void VariableCountKernel(int[] data, int n)
    {
        Gpu.ForUnroll(count: n, start: 0, body: i => { data[i] = i; });
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void BodyFirstReturnKernel(int[] data)
    {
        Gpu.ForUnroll(body: i =>
        {
            if (i == 2) return;
            data[i] += 1;
        }, start: 0, count: 4);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void GridStrideBodyFirstAssignmentKernel(int[] data)
    {
        Gpu.GridStrideLoop(body: i =>
        {
            i += 1;
            data[0] += i;
        }, maxBounds: data.Length);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ConstantReorderedKernel(int[] data)
    {
        Gpu.ForUnroll(count: 4, start: 0, body: i => { data[i] += 1; });
        Gpu.GridStrideLoop(maxBounds: data.Length, body: i => { data[i] += 1; });
    }
}
