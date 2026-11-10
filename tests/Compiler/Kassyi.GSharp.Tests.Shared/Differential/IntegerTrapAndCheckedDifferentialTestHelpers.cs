#nullable enable
using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Shared test constants, boundary value arrays, and naming utilities for integer trap and checked differential tests.
/// </summary>
internal static class IntegerTrapAndCheckedDifferentialTestHelpers
{
    internal const int DefaultRows = 64;

    internal static readonly long[] s_int64Boundaries =
    [
        0L, 1L, -1L, 2L, -2L, long.MaxValue, long.MinValue, long.MaxValue - 1L, long.MinValue + 1L,
        0x7FFF_FFFFL, -0x8000_0000L, 0x1_0000_0000L, -0x1_0000_0000L,
    ];

    internal static readonly ulong[] s_uint64Boundaries =
    [
        0UL, 1UL, 2UL, 0x7FFF_FFFFUL, 0x8000_0000UL, 0xFFFF_FFFFUL, 0x1_0000_0000UL,
        ulong.MaxValue - 1UL, ulong.MaxValue,
    ];

    internal static readonly double[] s_doubleBoundaries =
    [
        0.0, -0.0, 1.0, -1.0, 0.5, -0.5, double.MaxValue, double.MinValue,
        double.PositiveInfinity, double.NegativeInfinity, double.NaN,
        2147483647.0, 2147483648.0, -2147483648.0, -2147483649.0,
        4294967295.0, 4294967296.0, 9.223372036854776e18, -9.223372036854776e18,
    ];

    /// <summary>
    /// Builds the case name of a kernel of <see cref="IntegerTrapAndCheckedDifferentialKernels"/>.
    /// </summary>
    internal static string KernelName(string kernelMemberName, string? suffix = null) =>
        DiffKernelNames.Of(typeof(IntegerTrapAndCheckedDifferentialKernels), kernelMemberName, suffix);
}
