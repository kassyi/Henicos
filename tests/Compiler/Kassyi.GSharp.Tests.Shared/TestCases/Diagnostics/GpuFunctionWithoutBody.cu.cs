using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public static partial class GpuFunctionWithoutBodyKernels
{
    // GSH0003: no body to transpile
#pragma warning disable CS0626 // Method, operator, or accessor is marked external and has no attributes on it
    [GpuDeviceFunction]
    public static extern int External(int x);
#pragma warning restore CS0626

    // No diagnostic: C# removes the calls of a partial method without an implementing declaration, and so does G#
    [GpuDeviceFunction]
    static partial void NotImplemented(int[] data);

    // GSH0003: a kernel without an implementing declaration has no body to launch
    [GpuKernel]
    static partial void NotImplementedKernel(int[] data);

    // The implementing declaration below is the body: no diagnostic
    [GpuDeviceFunction]
    public static partial int Implemented(int x);

    public static partial int Implemented(int x) => x + 1;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CallerKernel(int[] data)
    {
        data[0] = Implemented(data[0]);
        NotImplemented(data);
    }
}
