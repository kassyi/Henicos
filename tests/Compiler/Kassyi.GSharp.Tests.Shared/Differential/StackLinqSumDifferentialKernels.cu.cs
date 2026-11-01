#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels that evaluate <c>StackLinq</c> <see cref="float"/> and <see cref="double"/> <c>Sum</c> overloads across
/// supported call forms (#730): direct <c>.AsStackEnumerable().Sum()</c>, selector <c>.Sum(x =&gt; ...)</c>, chained stages
/// (<c>Where</c>, <c>Select</c>, <c>Take</c>), and calls inside a <c>[GpuDeviceFunction]</c> (both direct and through a
/// transitive caller).
/// </summary>
public static partial class StackLinqSumDifferentialKernels
{
    /// <summary>The columns of <see cref="FloatSumMatrixKernel"/>.</summary>
    public static readonly string[] FloatColumns =
    [
        "direct",
        "selector",
        "select",
        "where",
        "take",
        "device_direct",
        "device_transitive"
    ];

    /// <summary>The columns of <see cref="DoubleSumMatrixKernel"/>.</summary>
    public static readonly string[] DoubleColumns =
    [
        "direct",
        "selector",
        "select",
        "where",
        "take",
        "device_direct",
        "device_transitive"
    ];

    [GpuDeviceFunction]
    public static float DeviceFloatSum(float[] data)
    {
        return data.AsStackEnumerable().Sum();
    }

    [GpuDeviceFunction]
    public static float DeviceFloatSumTransitive(float[] data)
    {
        return DeviceFloatSum(data) + data.AsStackEnumerable().Sum(x => x * 1.0f);
    }

    [GpuDeviceFunction]
    public static double DeviceDoubleSum(double[] data)
    {
        return data.AsStackEnumerable().Sum();
    }

    [GpuDeviceFunction]
    public static double DeviceDoubleSumTransitive(double[] data)
    {
        return DeviceDoubleSum(data) + data.AsStackEnumerable().Sum(x => x * 1.0);
    }

    [GpuKernel]
    public static void FloatSumMatrixKernel(float[] data, float[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t * 7 >= r.Length)
        {
            return;
        }

        int o = t * 7;
        r[o + 0] = data.AsStackEnumerable().Sum();
        r[o + 1] = data.AsStackEnumerable().Sum(x => x * 1.0f);
        r[o + 2] = data.AsStackEnumerable().Select(x => x * 1.0f).Sum();
        r[o + 3] = data.AsStackEnumerable().Where(x => x != 0.0f).Sum();
        r[o + 4] = data.AsStackEnumerable().Take(data.Length).Sum();
        r[o + 5] = DeviceFloatSum(data);
        r[o + 6] = DeviceFloatSumTransitive(data);
    }

    [GpuKernel]
    public static void DoubleSumMatrixKernel(double[] data, double[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t * 7 >= r.Length)
        {
            return;
        }

        int o = t * 7;
        r[o + 0] = data.AsStackEnumerable().Sum();
        r[o + 1] = data.AsStackEnumerable().Sum(x => x * 1.0);
        r[o + 2] = data.AsStackEnumerable().Select(x => x * 1.0).Sum();
        r[o + 3] = data.AsStackEnumerable().Where(x => x != 0.0).Sum();
        r[o + 4] = data.AsStackEnumerable().Take(data.Length).Sum();
        r[o + 5] = DeviceDoubleSum(data);
        r[o + 6] = DeviceDoubleSumTransitive(data);
    }
}
