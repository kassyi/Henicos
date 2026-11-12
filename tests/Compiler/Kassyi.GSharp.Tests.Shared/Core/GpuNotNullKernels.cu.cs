#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>A struct argument whose array field may be null.</summary>
public struct NotNullHolder
{
    public int[]? Data;
    public int Offset;
}

public static partial class GpuNotNullKernels
{
    // The nullable field is read once through Gpu.NotNull; the local is proven, so its reads and the call that passes
    // it to a non-nullable parameter have no null check of their own
    [GpuKernel]
    public static void SumKernel(NotNullHolder h, int[] o)
    {
        int[] data = Gpu.NotNull(h.Data);
        int sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i];
        }

        o[0] = sum + h.Offset;
        o[1] = First(data);
    }

    // In a device function, which has no error site table, the message names the place of the call
    [GpuKernel]
    public static void DeviceFunctionKernel(NotNullHolder h, int[] o)
    {
        o[0] = FirstOf(h);
    }

    // A null optional kernel argument reads as an empty array, but it is null: Gpu.NotNull throws for it
    [GpuKernel]
    public static void OptionalKernel(int[] o, int[]? bias)
    {
        o[0] = Gpu.NotNull(bias).Length;
    }

    [GpuDeviceFunction]
    private static int First(int[] data) => data[0];

    [GpuDeviceFunction]
    private static int FirstOf(NotNullHolder h) => Gpu.NotNull(h.Data)[0];

    // An empty array is not null
    [GpuKernel]
    public static void LengthKernel(NotNullHolder h, int[] o)
    {
        o[0] = Gpu.NotNull(h.Data).Length;
    }
}
