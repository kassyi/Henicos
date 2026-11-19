#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for integer <see cref="Math.Max(sbyte, sbyte)"/>, <see cref="Math.Min(sbyte, sbyte)"/>,
/// <see cref="Math.Clamp(sbyte, sbyte, sbyte)"/> and <see cref="Math.Sign(sbyte)"/> of every integer type, the
/// overloads C# picks for mixed operands, and narrow results passed by <see langword="ref"/>. Every kernel runs on
/// Managed, Native CPU, and CUDA against the C# oracle.
/// </summary>
/// <remarks>
/// <c>Math.Clamp</c> with <c>min &gt; max</c> throws <see cref="ArgumentException"/> in C#; native and CUDA do not
/// check it yet (#718), a known divergence. The clamp cases reverse the bounds of row 0 of one type per seed, from
/// seed 1 on.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class IntegerMinMaxClampDifferentialTests
{
    private const int MatrixRows = 256;
    private const int DefaultRows = 64;

    internal static readonly sbyte[] s_sbyteBoundaries =
    [
        0, 1, -1, 2, -2, -64, 64, sbyte.MinValue, sbyte.MinValue + 1, sbyte.MaxValue - 1, sbyte.MaxValue,
    ];

    internal static readonly byte[] s_byteBoundaries =
    [
        0, 1, 2, 127, 128, 254, byte.MaxValue - 1, byte.MaxValue,
    ];

    internal static readonly short[] s_int16Boundaries =
    [
        0, 1, -1, 2, -2, -128, 127, 255, 256, short.MinValue, short.MinValue + 1, short.MaxValue - 1, short.MaxValue,
    ];

    internal static readonly ushort[] s_uint16Boundaries =
    [
        0, 1, 2, 127, 128, 255, 256, 32767, 32768, ushort.MaxValue - 1, ushort.MaxValue,
    ];

    internal static readonly int[] s_int32Boundaries =
    [
        0, 1, -1, 2, -2, -128, 127, 0x7FFF, -0x8000, int.MinValue, int.MinValue + 1, int.MaxValue - 1, int.MaxValue,
    ];

    internal static readonly uint[] s_uint32Boundaries =
    [
        0u, 1u, 2u, 0x7FFF_FFFFu, 0x8000_0000u, 0x8000_0001u, 0xF000_0000u,
        0xFFFF_FFFEu, uint.MaxValue - 1u, uint.MaxValue,
    ];

    internal static readonly long[] s_int64Boundaries =
    [
        0L, 1L, -1L, 2L, -2L, -(1L << 31), 1L << 31, -(1L << 32), 1L << 32,
        -(1L << 62), 1L << 62, long.MinValue, long.MinValue + 1L, long.MaxValue - 1L, long.MaxValue,
    ];

    internal static readonly ulong[] s_uint64Boundaries =
    [
        0UL, 1UL, 2UL, 0x7FFF_FFFFUL, 0x8000_0000UL, 0x1_0000_0000UL, 0x7FFF_FFFF_FFFF_FFFFUL,
        0x8000_0000_0000_0000UL, 0x8000_0000_0000_0001UL, ulong.MaxValue - 1UL, ulong.MaxValue, 0xF000_0000_0000_0000UL,
    ];

    private static string KernelName(string memberName) =>
        DiffKernelNames.Of(typeof(IntegerMinMaxClampDifferentialKernels), memberName);

    internal static readonly DifferentialCase s_narrowMaxMin =
        new(KernelName(nameof(IntegerMinMaxClampDifferentialKernels.NarrowMaxMinKernel)))
    {
        Shape = LaunchShapes.Linear(MatrixRows, 32),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var (sbA, sbB) = DiffValues.Pairs(
                rng, rows, s_sbyteBoundaries, static r => unchecked((sbyte)r.NextInt32()));
            var (bA, bB) = DiffValues.Pairs(
                rng, rows, s_byteBoundaries, static r => unchecked((byte)r.NextUInt32()));
            var (sA, sB) = DiffValues.Pairs(
                rng, rows, s_int16Boundaries, static r => unchecked((short)r.NextInt32()));
            var (usA, usB) = DiffValues.Pairs(
                rng, rows, s_uint16Boundaries, static r => unchecked((ushort)r.NextUInt32()));
            return new DiffBuffers()
                .In("sbA", sbA, readOnly: true).In("sbB", sbB, readOnly: true)
                .In("bA", bA, readOnly: true).In("bB", bB, readOnly: true)
                .In("sA", sA, readOnly: true).In("sB", sB, readOnly: true)
                .In("usA", usA, readOnly: true).In("usB", usB, readOnly: true)
                .Out<sbyte>("sbOut", rows, IntegerMinMaxClampDifferentialKernels.MaxMinColumns, OutputPolicy.Exact)
                .Out<byte>("bOut", rows, IntegerMinMaxClampDifferentialKernels.MaxMinColumns, OutputPolicy.Exact)
                .Out<short>("sOut", rows, IntegerMinMaxClampDifferentialKernels.MaxMinColumns, OutputPolicy.Exact)
                .Out<ushort>("usOut", rows, IntegerMinMaxClampDifferentialKernels.MaxMinColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerMinMaxClampDifferentialKernels.LaunchNarrowMaxMinKernel(
            b.Get<sbyte>("sbA"), b.Get<sbyte>("sbB"),
            b.Get<byte>("bA"), b.Get<byte>("bB"),
            b.Get<short>("sA"), b.Get<short>("sB"),
            b.Get<ushort>("usA"), b.Get<ushort>("usB"),
            b.Get<sbyte>("sbOut"),
            b.Get<byte>("bOut"),
            b.Get<short>("sOut"),
            b.Get<ushort>("usOut"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_wideMaxMin =
        new(KernelName(nameof(IntegerMinMaxClampDifferentialKernels.WideMaxMinKernel)))
    {
        Shape = LaunchShapes.Linear(MatrixRows, 32),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var (uA, uB) = DiffValues.Pairs(rng, rows, s_uint32Boundaries, static r => r.NextUInt32());
            var (lA, lB) = DiffValues.Pairs(rng, rows, s_int64Boundaries, static r => unchecked((long)r.NextUInt64()));
            var (ulA, ulB) = DiffValues.Pairs(rng, rows, s_uint64Boundaries, static r => r.NextUInt64());
            return new DiffBuffers()
                .In("uA", uA, readOnly: true).In("uB", uB, readOnly: true)
                .In("lA", lA, readOnly: true).In("lB", lB, readOnly: true)
                .In("ulA", ulA, readOnly: true).In("ulB", ulB, readOnly: true)
                .Out<uint>("uOut", rows, IntegerMinMaxClampDifferentialKernels.MaxMinColumns, OutputPolicy.Exact)
                .Out<long>("lOut", rows, IntegerMinMaxClampDifferentialKernels.MaxMinColumns, OutputPolicy.Exact)
                .Out<ulong>("ulOut", rows, IntegerMinMaxClampDifferentialKernels.MaxMinColumns, OutputPolicy.Exact)
                .Out<long>("nOut", rows, IntegerMinMaxClampDifferentialKernels.MaxMinColumns, OutputPolicy.Exact)
                .Out<ulong>("nuOut", rows, IntegerMinMaxClampDifferentialKernels.MaxMinColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerMinMaxClampDifferentialKernels.LaunchWideMaxMinKernel(
            b.Get<uint>("uA"), b.Get<uint>("uB"),
            b.Get<long>("lA"), b.Get<long>("lB"),
            b.Get<ulong>("ulA"), b.Get<ulong>("ulB"),
            b.Get<uint>("uOut"),
            b.Get<long>("lOut"),
            b.Get<ulong>("ulOut"),
            b.Get<long>("nOut"),
            b.Get<ulong>("nuOut"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_narrowClamp =
        new(KernelName(nameof(IntegerMinMaxClampDifferentialKernels.NarrowClampKernel)))
    {
        Shape = LaunchShapes.Linear(MatrixRows, 32),
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var (sbV, sbLo, sbHi) = BuildClampTriples<sbyte>(
                rng, rows, s_sbyteBoundaries, reversedSeed: 1,
                static r => unchecked((sbyte)r.NextInt32()),
                (-50, -10, 20), (50, -10, 20), (5, -10, 20), (-20, 7, 7), (20, 7, 7), (-10, -10, 20), (20, -10, 20));
            var (bV, bLo, bHi) = BuildClampTriples<byte>(
                rng, rows, s_byteBoundaries, reversedSeed: 2,
                static r => unchecked((byte)r.NextUInt32()),
                (5, 20, 200), (250, 20, 200), (100, 20, 200), (10, 128, 128), (240, 128, 128), (20, 20, 200),
                (200, 20, 200));
            var (sV, sLo, sHi) = BuildClampTriples<short>(
                rng, rows, s_int16Boundaries, reversedSeed: 3,
                static r => unchecked((short)r.NextInt32()),
                (-5000, -1000, 2000), (5000, -1000, 2000), (500, -1000, 2000), (-200, 300, 300), (500, 300, 300),
                (-1000, -1000, 2000), (2000, -1000, 2000));
            var (usV, usLo, usHi) = BuildClampTriples<ushort>(
                rng, rows, s_uint16Boundaries, reversedSeed: 4,
                static r => unchecked((ushort)r.NextUInt32()),
                (100, 500, 40000), (60000, 500, 40000), (20000, 500, 40000), (1000, 32768, 32768),
                (50000, 32768, 32768), (500, 500, 40000), (40000, 500, 40000));
            return new DiffBuffers()
                .In("sbV", sbV, readOnly: true).In("sbLo", sbLo, readOnly: true).In("sbHi", sbHi, readOnly: true)
                .In("bV", bV, readOnly: true).In("bLo", bLo, readOnly: true).In("bHi", bHi, readOnly: true)
                .In("sV", sV, readOnly: true).In("sLo", sLo, readOnly: true).In("sHi", sHi, readOnly: true)
                .In("usV", usV, readOnly: true).In("usLo", usLo, readOnly: true).In("usHi", usHi, readOnly: true)
                .Out<sbyte>("sbOut", rows, ["clamp_sbyte"], OutputPolicy.Exact)
                .Out<byte>("bOut", rows, ["clamp_byte"], OutputPolicy.Exact)
                .Out<short>("sOut", rows, ["clamp_int16"], OutputPolicy.Exact)
                .Out<ushort>("usOut", rows, ["clamp_uint16"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerMinMaxClampDifferentialKernels.LaunchNarrowClampKernel(
            b.Get<sbyte>("sbV"), b.Get<sbyte>("sbLo"), b.Get<sbyte>("sbHi"),
            b.Get<byte>("bV"), b.Get<byte>("bLo"), b.Get<byte>("bHi"),
            b.Get<short>("sV"), b.Get<short>("sLo"), b.Get<short>("sHi"),
            b.Get<ushort>("usV"), b.Get<ushort>("usLo"), b.Get<ushort>("usHi"),
            b.Get<sbyte>("sbOut"),
            b.Get<byte>("bOut"),
            b.Get<short>("sOut"),
            b.Get<ushort>("usOut"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_wideClamp =
        new(KernelName(nameof(IntegerMinMaxClampDifferentialKernels.WideClampKernel)))
    {
        Shape = LaunchShapes.Linear(MatrixRows, 32),
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var (uV, uLo, uHi) = BuildClampTriples<uint>(
                rng, rows, s_uint32Boundaries, reversedSeed: 1,
                static r => r.NextUInt32(),
                (0x7FFF_FFFFu, 0x8000_0000u, 0xF000_0000u),
                (uint.MaxValue, 0x8000_0000u, 0xF000_0000u),
                (0xA000_0000u, 0x8000_0000u, 0xF000_0000u),
                (1u, 0x8000_0000u, 0x8000_0000u),
                (uint.MaxValue, 0x8000_0000u, 0x8000_0000u),
                (0x8000_0000u, 0x8000_0000u, 0xF000_0000u),
                (0xF000_0000u, 0x8000_0000u, 0xF000_0000u));
            var (lV, lLo, lHi) = BuildClampTriples<long>(
                rng, rows, s_int64Boundaries, reversedSeed: 2,
                static r => unchecked((long)r.NextUInt64()),
                (long.MinValue, -(1L << 40), 1L << 40),
                (long.MaxValue, -(1L << 40), 1L << 40),
                (0L, -(1L << 40), 1L << 40),
                (-(1L << 50), 123L, 123L),
                (1L << 50, 123L, 123L),
                (-(1L << 40), -(1L << 40), 1L << 40),
                (1L << 40, -(1L << 40), 1L << 40));
            var (ulV, ulLo, ulHi) = BuildClampTriples<ulong>(
                rng, rows, s_uint64Boundaries, reversedSeed: 3,
                static r => r.NextUInt64(),
                (0x7FFF_FFFF_FFFF_FFFFUL, 0x8000_0000_0000_0000UL, 0xF000_0000_0000_0000UL),
                (ulong.MaxValue, 0x8000_0000_0000_0000UL, 0xF000_0000_0000_0000UL),
                (0xA000_0000_0000_0000UL, 0x8000_0000_0000_0000UL, 0xF000_0000_0000_0000UL),
                (1UL, 0x8000_0000_0000_0000UL, 0x8000_0000_0000_0000UL),
                (ulong.MaxValue, 0x8000_0000_0000_0000UL, 0x8000_0000_0000_0000UL),
                (0x8000_0000_0000_0000UL, 0x8000_0000_0000_0000UL, 0xF000_0000_0000_0000UL),
                (0xF000_0000_0000_0000UL, 0x8000_0000_0000_0000UL, 0xF000_0000_0000_0000UL));
            return new DiffBuffers()
                .In("uV", uV, readOnly: true).In("uLo", uLo, readOnly: true).In("uHi", uHi, readOnly: true)
                .In("lV", lV, readOnly: true).In("lLo", lLo, readOnly: true).In("lHi", lHi, readOnly: true)
                .In("ulV", ulV, readOnly: true).In("ulLo", ulLo, readOnly: true).In("ulHi", ulHi, readOnly: true)
                .Out<uint>("uOut", rows, ["clamp_uint32"], OutputPolicy.Exact)
                .Out<long>("lOut", rows, ["clamp_int64"], OutputPolicy.Exact)
                .Out<ulong>("ulOut", rows, ["clamp_uint64"], OutputPolicy.Exact)
                .Out<long>("nOut", rows, ["clamp_nint"], OutputPolicy.Exact)
                .Out<ulong>("nuOut", rows, ["clamp_nuint"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerMinMaxClampDifferentialKernels.LaunchWideClampKernel(
            b.Get<uint>("uV"), b.Get<uint>("uLo"), b.Get<uint>("uHi"),
            b.Get<long>("lV"), b.Get<long>("lLo"), b.Get<long>("lHi"),
            b.Get<ulong>("ulV"), b.Get<ulong>("ulLo"), b.Get<ulong>("ulHi"),
            b.Get<uint>("uOut"),
            b.Get<long>("lOut"),
            b.Get<ulong>("ulOut"),
            b.Get<long>("nOut"),
            b.Get<ulong>("nuOut"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_integerSign =
        new(KernelName(nameof(IntegerMinMaxClampDifferentialKernels.IntegerSignKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var sbV = DiffValues.Values(rng, rows, s_sbyteBoundaries, static r => unchecked((sbyte)r.NextInt32()));
            var sV = DiffValues.Values(rng, rows, s_int16Boundaries, static r => unchecked((short)r.NextInt32()));
            var iV = DiffValues.Values(rng, rows, s_int32Boundaries, static r => r.NextInt32());
            var lV = DiffValues.Values(rng, rows, s_int64Boundaries, static r => unchecked((long)r.NextUInt64()));
            return new DiffBuffers()
                .In("sbV", sbV, readOnly: true)
                .In("sV", sV, readOnly: true)
                .In("iV", iV, readOnly: true)
                .In("lV", lV, readOnly: true)
                .Out<int>("dst", rows, IntegerMinMaxClampDifferentialKernels.SignColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerMinMaxClampDifferentialKernels.LaunchIntegerSignKernel(
            b.Get<sbyte>("sbV"),
            b.Get<short>("sV"),
            b.Get<int>("iV"),
            b.Get<long>("lV"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    private static readonly long[] s_mixedLong =
        [long.MinValue, -6, -5, -1, 0, 1, 3, 4, 5, 6, long.MaxValue, 1L << 40];

    private static readonly byte[] s_mixedByte = [0, 1, 2, 9, 10, 64, 128, 199, 200, 201, 254, 255, 3];

    private static readonly ushort[] s_mixedUInt16 = [0, 1, 3, 255, 256, 257, 32768, 32769, 65534, 65535, 1000];

    private static readonly sbyte[] s_mixedSByte = [sbyte.MinValue, -64, -1, 0, 1, 64, 126, sbyte.MaxValue, -2];

    private static readonly uint[] s_mixedUInt32 =
        [0u, 1u, 0x7FFF_FFFFu, 0x8000_0000u, uint.MaxValue, 5u, 0xFFFF_FFF0u];

    private static readonly int[] s_mixedInt32 = [int.MinValue, -1, 0, 1, int.MaxValue, -7, 100, 65536];

    private static readonly short[] s_mixedInt16 = [short.MinValue, -1, 0, 1, short.MaxValue, 32766, 255];

    // The lengths are nearly coprime, so the cycled tables meet in many combinations
    private static T[] Cycle<T>(T[] table, int rows)
    {
        var values = new T[rows];
        for (int i = 0; i < rows; i++)
        {
            values[i] = table[i % table.Length];
        }

        return values;
    }

    internal static readonly DifferentialCase s_mixedOperand =
        new(KernelName(nameof(IntegerMinMaxClampDifferentialKernels.MixedOperandKernel)))
    {
        Shape = LaunchShapes.Linear(128, 32),
        Seeds = [0],
        Inputs = static (_, shape) => new DiffBuffers()
            .In("l", Cycle(s_mixedLong, shape.Rows), readOnly: true)
            .In("b", Cycle(s_mixedByte, shape.Rows), readOnly: true)
            .In("us", Cycle(s_mixedUInt16, shape.Rows), readOnly: true)
            .In("sb", Cycle(s_mixedSByte, shape.Rows), readOnly: true)
            .In("u", Cycle(s_mixedUInt32, shape.Rows), readOnly: true)
            .In("iv", Cycle(s_mixedInt32, shape.Rows), readOnly: true)
            .In("s", Cycle(s_mixedInt16, shape.Rows), readOnly: true)
            .Out<long>("r", shape.Rows, IntegerMinMaxClampDifferentialKernels.MixedOperandColumns, OutputPolicy.Exact),
        Launch = static (b, s) => IntegerMinMaxClampDifferentialKernels.LaunchMixedOperandKernel(
            b.Get<long>("l"),
            b.Get<byte>("b"),
            b.Get<ushort>("us"),
            b.Get<sbyte>("sb"),
            b.Get<uint>("u"),
            b.Get<int>("iv"),
            b.Get<short>("s"),
            b.Get<long>("r"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    private static (T[] Values, T[] Low, T[] High) BuildClampTriples<T>(
        DiffRandom rng,
        int rows,
        T[] boundaries,
        int reversedSeed,
        Func<DiffRandom, T> next,
        params (T Value, T Lo, T Hi)[] witnesses)
    {
        var values = DiffValues.Values(rng, rows, boundaries, next);
        var (b1, b2) = DiffValues.Pairs(rng, rows, boundaries, next);
        var low = new T[rows];
        var high = new T[rows];
        var comparer = Comparer<T>.Default;
        for (int r = 0; r < rows; r++)
        {
            if (comparer.Compare(b1[r], b2[r]) <= 0)
            {
                low[r] = b1[r];
                high[r] = b2[r];
            }
            else
            {
                low[r] = b2[r];
                high[r] = b1[r];
            }
        }

        for (int i = 0; i < witnesses.Length && i < rows; i++)
        {
            values[i] = witnesses[i].Value;
            low[i] = witnesses[i].Lo;
            high[i] = witnesses[i].Hi;
        }

        // min > max: C# throws ArgumentException (a known divergence on native and CUDA, #718)
        if (rng.Seed == reversedSeed)
        {
            (low[0], high[0]) = (high[0], low[0]);
        }

        return (values, low, high);
    }

    // Narrow Math.Max and Math.Min (sbyte, byte, short, ushort) match .NET on every pair of boundaries.
    // mutant: Prelude/IntegerSemantics.hpp `__gsharp_max_int` `b > a ? b : a` -> `a > b ? b : a`
    // mutant: Prelude/IntegerSemantics.hpp `__gsharp_min_int` `b < a ? b : a` -> `a < b ? b : a`
    // input: seed 0 cross product of each boundary table (MinValue against MaxValue among them)
    [SkippableFact]
    public void IntegerMath_NarrowMaxMin_MatchesTheOracleOnEveryPath()
    {
        Assert.True(MatrixRows >= s_sbyteBoundaries.Length * s_sbyteBoundaries.Length);
        Assert.True(MatrixRows >= s_byteBoundaries.Length * s_byteBoundaries.Length);
        Assert.True(MatrixRows >= s_int16Boundaries.Length * s_int16Boundaries.Length);
        Assert.True(MatrixRows >= s_uint16Boundaries.Length * s_uint16Boundaries.Length);
        DifferentialRunner.Run(s_narrowMaxMin);
    }

    // Wide Math.Max and Math.Min (uint, long, ulong, nint, nuint) match .NET including unsigned high-bit pairs.
    // mutant: Prelude/IntegerSemantics.hpp `__gsharp_max_int` `b > a ? b : a` -> `a > b ? b : a`
    // mutant: Prelude/IntegerSemantics.hpp `__gsharp_min_int` `b < a ? b : a` -> `a < b ? b : a`
    // input: seed 0 cross product including uint (0x7FFF_FFFF, 0x8000_0000) and long (MinValue, MaxValue)
    [SkippableFact]
    public void IntegerMath_WideMaxMin_MatchesTheOracleOnEveryPath()
    {
        Assert.True(MatrixRows >= s_uint32Boundaries.Length * s_uint32Boundaries.Length);
        Assert.True(MatrixRows >= s_int64Boundaries.Length * s_int64Boundaries.Length);
        Assert.True(MatrixRows >= s_uint64Boundaries.Length * s_uint64Boundaries.Length);
        DifferentialRunner.Run(s_wideMaxMin);
    }

    // Narrow Math.Clamp (sbyte, byte, short, ushort) with lo <= hi matches .NET on all three branches.
    // mutant: Prelude.cu `__gsharp_clamp` template `value < min_val ? min_val` -> `value < min_val ? max_val`
    // input: rows 0..6 (value below, above, inside, lo == hi on both sides, value == lo, value == hi)
    [SkippableFact]
    public void IntegerMath_NarrowClamp_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_narrowClamp);

    // Wide Math.Clamp (uint, long, ulong, nint, nuint) with lo <= hi matches .NET including uint >= 0x8000_0000u.
    // mutant: Prelude.cu `__gsharp_clamp` template `value < min_val ? min_val` -> `value < min_val ? max_val`
    // input: rows 0..6 including uint 0x8000_0000..0xF000_0000 bounds
    [SkippableFact]
    public void IntegerMath_WideClamp_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_wideClamp);

    // Math.Sign for signed integer types (sbyte, short, int, long, nint) returns -1, 0, or 1 matching .NET.
    // mutant: Prelude/IntegerSemantics.hpp `__gsharp_sign_int` remove `- (int)(a < 0)`
    // input: negative boundary values (-1, -2, MinValue) across all five signed types
    [SkippableFact]
    public void IntegerMath_Sign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_integerSign);

    // Mixed operands are converted to the parameter type C# chose, so the templates deduce one type; narrow results
    // keep their type when passed by ref.
    // mutant: StandardCpp.yml `System.Math.Max(byte, byte)` -> "max" (CUDA's max gives an int: the ref row fails
    // to build)
    // input: the cycled tables above, 128 rows
    [SkippableFact]
    public void IntegerMath_MixedOperandsAndNarrowResults_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mixedOperand);
}
