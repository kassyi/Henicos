using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

[SkipSnapshotConsistency("Local initialization checked by EmissionShapeTests")]
public static partial class SkipLocalsInitKernels
{
    [GpuKernel]
    public static void ZeroInitializedKernel(int[] output, int n)
    {
        int value = default;
        value += n;
        output[0] = value;
    }

    [GpuKernel]
    [SkipLocalsInit]
    public static void MethodSkipsKernel(int[] output, int n)
    {
        int value = default;
        value += n;
        output[0] = value;
    }
}

[SkipLocalsInit]
[SkipSnapshotConsistency("Local initialization checked by EmissionShapeTests")]
public static partial class SkipLocalsInitTypeKernels
{
    [GpuKernel]
    public static void TypeSkipsKernel(int[] output, int n)
    {
        int value = default;
        value += n;
        output[0] = value;
    }
}
