using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0011: the body of a loop macro has no loop variable. Such a lambda does not convert to the macro's delegate
// (CS1593), so the call binds to no method and no macro rule sees it; the case records that nothing reports GSH0011.
public static partial class MacroLambdaWithoutParameterKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ForUnrollWithoutParameterKernel(int[] data)
    {
        Gpu.ForUnroll(0, 4, () =>
        {
            data[0] += 1;
        });
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void GridStrideLoopWithoutParameterKernel(int[] data)
    {
        Gpu.GridStrideLoop(data.Length, () =>
        {
            data[0] += 1;
        });
    }
}
