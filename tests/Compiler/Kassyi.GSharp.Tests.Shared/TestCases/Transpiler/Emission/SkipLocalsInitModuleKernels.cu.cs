using System;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

[module: SkipLocalsInit]

namespace Kassyi.GSharp.Tests.TestCases.Emission;

[SkipSnapshotConsistency("Local initialization checked by EmissionShapeTests")]
public static partial class SkipLocalsInitModuleKernels
{
    [GpuKernel]
    public static void ModuleSkipsKernel(int[] output, int n)
    {
        int value = default;
        value += n;
        output[0] = value;
        Span<int> moduleBuf = stackalloc int[4];
        int[] moduleArray = new int[4];
        moduleBuf[0] = n;
        output[1] = moduleBuf[0] + moduleArray[n & 3];
    }
}
