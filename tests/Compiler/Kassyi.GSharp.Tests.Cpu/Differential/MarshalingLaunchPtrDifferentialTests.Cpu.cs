#nullable enable
using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial class MarshalingLaunchPtrDifferentialTests
{
    private static partial Action<DiffBuffers, LaunchShape> GetPtrLengthGuardedScaleLaunch() =>
        static (b, s) =>
        {
            int[] data = b.Get<int>("data");
            int[] outLen = b.Get<int>("outLen");
            MarshalingAliasingDifferentialKernels.LaunchPtrLengthGuardedScaleKernel(
                data, 3, outLen, gridDimX: s.GridX, blockDimX: s.BlockX);
        };

    private static partial Action<DiffBuffers, LaunchShape>? GetOverloadedLaunchPtr() => null;
    private static partial Action<DiffBuffers, LaunchShape>? GetPtrConvertStructAndRefLaunchPtr() => null;
}
