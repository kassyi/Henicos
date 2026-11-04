#nullable enable
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Diagnostics;

public static partial class NullableVector2DirectKernels
{
    [GpuKernel]
    public static Vector2? NullableVec2AllDirectKernel(
        Vector2? v,
        ref Vector2? rv,
        Vector2?[] arr)
    {
        rv = v;
        return arr[0];
    }
}
