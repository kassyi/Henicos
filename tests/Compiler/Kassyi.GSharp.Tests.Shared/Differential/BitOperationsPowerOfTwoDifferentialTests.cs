#nullable enable
using System.Numerics;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for signed <see cref="BitOperations.TrailingZeroCount(int)"/>, all
/// <see cref="BitOperations.IsPow2(int)"/> overloads, and unsigned <see cref="BitOperations.RoundUpToPowerOf2(uint)"/>.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class BitOperationsPowerOfTwoDifferentialTests
{
    private const int DefaultRows = 64;

    internal static readonly int[] s_tzcInt32Boundaries =
    [
        0, 1, -1, 2, -2, 8, -8, 0x4000_0000, -0x4000_0000, int.MaxValue, int.MinValue + 1, int.MinValue,
    ];

    internal static readonly long[] s_tzcInt64Boundaries =
    [
        0L, 1L, -1L, 2L, -2L, 8L, -8L, 1L << 31, -(1L << 31), 1L << 32, -(1L << 32),
        1L << 62, -(1L << 62), long.MaxValue, long.MinValue + 1L, long.MinValue,
    ];

    internal static readonly int[] s_isPow2Int32Boundaries =
    [
        0, 1, -1, 2, -2, 3, 4, -4, 8, -8,
        0x4000_0000 - 1, 0x4000_0000, 0x4000_0000 + 1, int.MaxValue, int.MinValue + 1, int.MinValue,
    ];

    internal static readonly uint[] s_isPow2UInt32Boundaries =
    [
        0u, 1u, 2u, 3u, 4u, 0x4000_0000u, 0x7FFF_FFFFu, 0x8000_0000u, 0x8000_0001u, uint.MaxValue - 1u, uint.MaxValue,
    ];

    internal static readonly long[] s_isPow2Int64Boundaries =
    [
        0L, 1L, -1L, 2L, -2L, 3L, 4L, -4L, 8L, -8L,
        (1L << 31) - 1, 1L << 31, (1L << 31) + 1, -(1L << 31),
        (1L << 62) - 1, 1L << 62, (1L << 62) + 1, -(1L << 62),
        long.MaxValue, long.MinValue + 1L, long.MinValue,
    ];

    internal static readonly ulong[] s_isPow2UInt64Boundaries =
    [
        0UL, 1UL, 2UL, 3UL, 4UL,
        (1UL << 31) - 1, 1UL << 31, (1UL << 31) + 1,
        (1UL << 62) - 1, 1UL << 62, (1UL << 62) + 1,
        (1UL << 63) - 1, 1UL << 63, (1UL << 63) + 1,
        ulong.MaxValue - 1UL, ulong.MaxValue,
    ];

    internal static readonly uint[] s_roundUpUInt32Boundaries =
    [
        0u, 1u, 2u, 3u, 4u, 5u, 7u, 8u, 9u,
        0x3FFF_FFFFu, 0x4000_0000u, 0x4000_0001u,
        0x7FFF_FFFFu, 0x8000_0000u, 0x8000_0001u, uint.MaxValue - 1u, uint.MaxValue,
    ];

    internal static readonly ulong[] s_roundUpUInt64Boundaries =
    [
        0UL, 1UL, 2UL, 3UL, 4UL, 5UL,
        1UL << 31, (1UL << 31) + 1, 1UL << 32,
        (1UL << 62) - 1, 1UL << 62, (1UL << 62) + 1,
        (1UL << 63) - 1, 1UL << 63, (1UL << 63) + 1,
        ulong.MaxValue - 1UL, ulong.MaxValue,
    ];

    private static string KernelName(string memberName) =>
        DiffKernelNames.Of(typeof(BitOperationsPowerOfTwoDifferentialKernels), memberName);

    internal static readonly DifferentialCase s_trailingZeroCountSigned = DiffCases.CreateRows<int, long, int>(
        BitOperationsPowerOfTwoDifferentialKernels.LaunchTrailingZeroCountSignedKernel,
        inputs: static (rng, shape) =>
        {
            int rows = shape.Rows;
            var iVals = DiffValues.Values(rng, rows, s_tzcInt32Boundaries, static r => r.NextInt32());
            var lVals = DiffValues.Values(rng, rows, s_tzcInt64Boundaries, static r => unchecked((long)r.NextUInt64()));
            return new DiffBuffers()
                .In("iVals", iVals, readOnly: true)
                .In("lVals", lVals, readOnly: true)
                .Out<int>("dst", rows, BitOperationsPowerOfTwoDifferentialKernels.TrailingZeroCountColumns,
                    OutputPolicy.Exact);
        },
        shape: LaunchShapes.Linear(DefaultRows, 32),
        firstInputName: "iVals",
        secondInputName: "lVals");

    internal static readonly DifferentialCase s_isPow2AllOverloads =
        new(KernelName(nameof(BitOperationsPowerOfTwoDifferentialKernels.IsPow2AllOverloadsKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var iVals = DiffValues.Values(rng, rows, s_isPow2Int32Boundaries, static r => r.NextInt32());
            var uVals = DiffValues.Values(rng, rows, s_isPow2UInt32Boundaries, static r => r.NextUInt32());
            var lVals = DiffValues.Values(
                rng, rows, s_isPow2Int64Boundaries, static r => unchecked((long)r.NextUInt64()));
            var ulVals = DiffValues.Values(rng, rows, s_isPow2UInt64Boundaries, static r => r.NextUInt64());
            return new DiffBuffers()
                .In("iVals", iVals, readOnly: true)
                .In("uVals", uVals, readOnly: true)
                .In("lVals", lVals, readOnly: true)
                .In("ulVals", ulVals, readOnly: true)
                .Out<bool>("dst", rows, BitOperationsPowerOfTwoDifferentialKernels.IsPow2Columns,
                    OutputPolicy.Exact);
        },
        Launch = static (b, s) => BitOperationsPowerOfTwoDifferentialKernels.LaunchIsPow2AllOverloadsKernel(
            operands: new PowerOfTwoCheckOperands
            {
                Int32Values = b.Get<int>("iVals"),
                UInt32Values = b.Get<uint>("uVals"),
                Int64Values = b.Get<long>("lVals"),
                UInt64Values = b.Get<ulong>("ulVals"),
            },
            results: b.Get<bool>("dst"),
            rows: s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_roundUpToPowerOf2Unsigned =
        new(KernelName(nameof(BitOperationsPowerOfTwoDifferentialKernels.RoundUpToPowerOf2UnsignedKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var uVals = DiffValues.Values(rng, rows, s_roundUpUInt32Boundaries, static r => r.NextUInt32());
            var ulVals = DiffValues.Values(rng, rows, s_roundUpUInt64Boundaries, static r => r.NextUInt64());
            return new DiffBuffers()
                .In("uVals", uVals, readOnly: true)
                .In("ulVals", ulVals, readOnly: true)
                .Out<uint>("uOut", rows, ["round_up_uint32"], OutputPolicy.Exact)
                .Out<ulong>("ulOut", rows, ["round_up_uint64"], OutputPolicy.Exact)
                .Out<ulong>("nuOut", rows, ["round_up_nuint"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BitOperationsPowerOfTwoDifferentialKernels.LaunchRoundUpToPowerOf2UnsignedKernel(
            operands: new UnsignedPowerOfTwoOperands
            {
                UInt32Values = b.Get<uint>("uVals"),
                UInt64Values = b.Get<ulong>("ulVals"),
            },
            results: new UnsignedPowerOfTwoResults
            {
                UInt32Results = b.Get<uint>("uOut"),
                UInt64Results = b.Get<ulong>("ulOut"),
                NUIntResults = b.Get<ulong>("nuOut"),
            },
            rows: s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    // Signed BitOperations.TrailingZeroCount (int, long, nint) matches .NET bit counts including 0 and MinValue.
    // mutant: BclMappings.cs `TrailingZeroCount(System.Int32)` -> `__gsharp_tzc_64` (0 gives 64 instead of 32)
    // input: 0, -1, MinValue of each width
    [SkippableFact]
    public void BitOperations_TrailingZeroCountSigned_MatchesTheOracleOnEveryPath()
    {
        Assert.True(DefaultRows >= s_tzcInt64Boundaries.Length);
        DifferentialRunner.Run(s_trailingZeroCountSigned);
    }

    // BitOperations.IsPow2 across signed and unsigned 32/64/native overloads matches .NET without MinValue - 1 UB.
    // mutant: Prelude/IntegerSemantics.hpp `__gsharp_is_pow2_i32` remove `v > 0 &&`
    // mutant: Prelude/IntegerSemantics.hpp `__gsharp_is_pow2_i64` remove `v > 0 &&`
    // mutant: Prelude/IntegerSemantics.hpp `__gsharp_is_pow2_u32` remove `v != 0u &&`
    // mutant: BclMappings.cs `IsPow2(System.IntPtr)` -> `__gsharp_is_pow2_u64` (nint MinValue gives true)
    // input: 0, int.MinValue, long.MinValue, and (nint)long.MinValue (NOT -8, which stays false)
    [SkippableFact]
    public void BitOperations_IsPow2_MatchesTheOracleOnEveryPath()
    {
        Assert.True(DefaultRows >= s_isPow2Int64Boundaries.Length);
        DifferentialRunner.Run(s_isPow2AllOverloads);
    }

    // BitOperations.RoundUpToPowerOf2 (uint, ulong, nuint) matches .NET for 0, powers of 2, and > 2^(W-1) wrap to 0.
    // mutant: Prelude.cu / Prelude/MathAndBitOps.hpp `__gsharp_round_up_pow2_32` `value - 1u` -> `value`
    // mutant: Prelude.cu / Prelude/MathAndBitOps.hpp `__gsharp_round_up_pow2_64` `: 0ull` -> `: 1ull`
    // input: 0, exact powers of 2 (1, 2, 4, 0x4000_0000, 1UL<<62, 1UL<<63), and 2^31+1 / 2^63+1 -> 0
    [SkippableFact]
    public void BitOperations_RoundUpToPowerOf2_MatchesTheOracleOnEveryPath()
    {
        Assert.True(DefaultRows >= s_roundUpUInt32Boundaries.Length);
        Assert.True(DefaultRows >= s_roundUpUInt64Boundaries.Length);
        DifferentialRunner.Run(s_roundUpToPowerOf2Unsigned);
    }
}
