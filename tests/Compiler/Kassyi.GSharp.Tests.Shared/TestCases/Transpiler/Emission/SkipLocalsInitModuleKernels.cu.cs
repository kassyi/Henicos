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
    }
}
