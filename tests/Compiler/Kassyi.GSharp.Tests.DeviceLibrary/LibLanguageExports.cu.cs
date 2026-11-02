#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.DeviceLibrary;

/// <summary>
/// A cross-assembly <see cref="IDisposable"/> struct exported from <c>Kassyi.GSharp.Tests.DeviceLibrary</c>
/// to verify <c>using</c> disposal across assembly boundaries.
/// </summary>
public struct LibDisposableGuard : IDisposable
{
    public int[] Log;
    public int Slot;
    public int Marker;

    public void Dispose()
    {
        Log[Slot] = Marker + 777;
    }
}

/// <summary>
/// Additional cross-assembly <c>[GpuDeviceFunction]</c> overloads and extension methods for Lane D6 differential testing.
/// </summary>
public static partial class LibLanguageExports
{
    [GpuDeviceFunction]
    public static int CombineOverload(int a, int b) => (a * 3) + b + 11;

    [GpuDeviceFunction]
    public static long CombineOverload(long a, long b) => (a * 5L) - b + 23L;

    [GpuDeviceFunction]
    public static int WeightedDifference(this Pair pair, int scale) => (pair.A * scale) - pair.B;
}
