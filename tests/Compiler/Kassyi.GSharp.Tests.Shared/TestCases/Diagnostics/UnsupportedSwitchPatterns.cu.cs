using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct SwitchPoint
{
    public int X;
    public int Y;
}

// GSH0003: switch expression arms whose pattern is not a constant or a discard
public static partial class UnsupportedSwitchPatternKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void PropertyPatternKernel(SwitchPoint[] points, int[] output)
    {
        SwitchPoint p = points[0];
        output[0] = p switch
        {
            { X: 0, Y: 0 } => 1,
            _ => 0
        };
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ListPatternKernel(int[] data, int[] output)
    {
        output[0] = data switch
        {
            [1, 2, ..] => 10,
            _ => 0
        };
    }
}
