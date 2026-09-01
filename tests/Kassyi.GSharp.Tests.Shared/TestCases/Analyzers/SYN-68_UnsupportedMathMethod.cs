using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN_68_UnsupportedMathMethod
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithUnsupportedMath(int[] data)
    {
        // Calling unmapped Math method (e.g. Math.ScaleB) in kernel triggers unsupported syntax diagnostic
        double result = Math.ScaleB(1.0, 2);
        data[0] = (int)result;
    }
}
