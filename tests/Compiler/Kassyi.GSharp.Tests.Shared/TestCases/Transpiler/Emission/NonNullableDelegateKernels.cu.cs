#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

// A method group of a device function whose calls check a non-nullable array argument (#1409): a call through the
// delegate could not check it, so the reference is GSH0003
[SkipSnapshotConsistency("The method group is rejected; checked by EmissionShapeTests")]
public static partial class NonNullableDelegateKernels
{
    [GpuKernel]
    public static void DelegateKernel(int[] o)
    {
        System.Func<int[], int> read = ReadFirst;
        o[0] = read(o);
    }

    [GpuDeviceFunction]
    private static int ReadFirst(int[] a) => a[0];
}
