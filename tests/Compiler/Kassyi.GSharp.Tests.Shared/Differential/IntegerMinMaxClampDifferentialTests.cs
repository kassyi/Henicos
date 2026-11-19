#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using K = Kassyi.GSharp.Tests.Shared.Differential.IntegerMinMaxClampDifferentialKernels;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for integer <see cref="Math.Max(sbyte, sbyte)"/>, <see cref="Math.Min(sbyte, sbyte)"/> and
/// <see cref="Math.Clamp(sbyte, sbyte, sbyte)"/> of every integer type, one case per type,
/// <see cref="Math.Sign(sbyte)"/>, the overloads C# picks for mixed operands, narrow results, and narrow arguments.
/// Every kernel runs on Managed, Native CPU, and CUDA against the C# oracle.
/// </summary>
/// <remarks>
/// <c>Math.Clamp</c> with <c>min &gt; max</c> throws <see cref="ArgumentException"/> in C#; native and CUDA do not
/// check it yet (#718), a known divergence. Each type has its own <c>ReversedBounds</c> case for it, so the known
/// divergence covers no value case.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class IntegerMinMaxClampDifferentialTests
{
    private const int MatrixRows = 256;
    private const int DefaultRows = 64;
    private const string ReversedBounds = "ReversedBounds";

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

    // Rows 0..6 of every clamp case: value below, above and inside the bounds, lo == hi from both sides, value == lo,
    // value == hi
    private static readonly (sbyte, sbyte, sbyte)[] s_sbyteClampRows =
        [(-50, -10, 20), (50, -10, 20), (5, -10, 20), (-20, 7, 7), (20, 7, 7), (-10, -10, 20), (20, -10, 20)];

    private static readonly (byte, byte, byte)[] s_byteClampRows =
        [(5, 20, 200), (250, 20, 200), (100, 20, 200), (10, 128, 128), (240, 128, 128), (20, 20, 200), (200, 20, 200)];

    private static readonly (short, short, short)[] s_int16ClampRows =
    [
        (-5000, -1000, 2000), (5000, -1000, 2000), (500, -1000, 2000), (-200, 300, 300), (500, 300, 300),
        (-1000, -1000, 2000), (2000, -1000, 2000),
    ];

    private static readonly (ushort, ushort, ushort)[] s_uint16ClampRows =
    [
        (100, 500, 40000), (60000, 500, 40000), (20000, 500, 40000), (1000, 32768, 32768), (50000, 32768, 32768),
        (500, 500, 40000), (40000, 500, 40000),
    ];

    private static readonly (uint, uint, uint)[] s_uint32ClampRows =
    [
        (0x7FFF_FFFFu, 0x8000_0000u, 0xF000_0000u), (uint.MaxValue, 0x8000_0000u, 0xF000_0000u),
        (0xA000_0000u, 0x8000_0000u, 0xF000_0000u), (1u, 0x8000_0000u, 0x8000_0000u),
        (uint.MaxValue, 0x8000_0000u, 0x8000_0000u), (0x8000_0000u, 0x8000_0000u, 0xF000_0000u),
        (0xF000_0000u, 0x8000_0000u, 0xF000_0000u),
    ];

    private static readonly (long, long, long)[] s_int64ClampRows =
    [
        (long.MinValue, -(1L << 40), 1L << 40), (long.MaxValue, -(1L << 40), 1L << 40), (0L, -(1L << 40), 1L << 40),
        (-(1L << 50), 123L, 123L), (1L << 50, 123L, 123L), (-(1L << 40), -(1L << 40), 1L << 40),
        (1L << 40, -(1L << 40), 1L << 40),
    ];

    private static readonly (ulong, ulong, ulong)[] s_uint64ClampRows =
    [
        (0x7FFF_FFFF_FFFF_FFFFUL, 0x8000_0000_0000_0000UL, 0xF000_0000_0000_0000UL),
        (ulong.MaxValue, 0x8000_0000_0000_0000UL, 0xF000_0000_0000_0000UL),
        (0xA000_0000_0000_0000UL, 0x8000_0000_0000_0000UL, 0xF000_0000_0000_0000UL),
        (1UL, 0x8000_0000_0000_0000UL, 0x8000_0000_0000_0000UL),
        (ulong.MaxValue, 0x8000_0000_0000_0000UL, 0x8000_0000_0000_0000UL),
        (0x8000_0000_0000_0000UL, 0x8000_0000_0000_0000UL, 0xF000_0000_0000_0000UL),
        (0xF000_0000_0000_0000UL, 0x8000_0000_0000_0000UL, 0xF000_0000_0000_0000UL),
    ];

    private static readonly Func<DiffRandom, sbyte> s_nextSByte = static r => unchecked((sbyte)r.NextInt32());
    private static readonly Func<DiffRandom, byte> s_nextByte = static r => unchecked((byte)r.NextUInt32());
    private static readonly Func<DiffRandom, short> s_nextInt16 = static r => unchecked((short)r.NextInt32());
    private static readonly Func<DiffRandom, ushort> s_nextUInt16 = static r => unchecked((ushort)r.NextUInt32());
    private static readonly Func<DiffRandom, uint> s_nextUInt32 = static r => r.NextUInt32();
    private static readonly Func<DiffRandom, long> s_nextInt64 = static r => unchecked((long)r.NextUInt64());
    private static readonly Func<DiffRandom, ulong> s_nextUInt64 = static r => r.NextUInt64();

    // Max and Min of one type: seed 0 is the cross product of its boundary table
    internal static readonly DifferentialCase[] s_maxMinCases =
    [
        MaxMinCase(nameof(K.MaxMinSByteKernel), s_sbyteBoundaries, s_nextSByte,
            static (a, b, d, s) =>
                K.LaunchMaxMinSByteKernel(a, b, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        MaxMinCase(nameof(K.MaxMinByteKernel), s_byteBoundaries, s_nextByte,
            static (a, b, d, s) =>
                K.LaunchMaxMinByteKernel(a, b, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        MaxMinCase(nameof(K.MaxMinInt16Kernel), s_int16Boundaries, s_nextInt16,
            static (a, b, d, s) =>
                K.LaunchMaxMinInt16Kernel(a, b, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        MaxMinCase(nameof(K.MaxMinUInt16Kernel), s_uint16Boundaries, s_nextUInt16,
            static (a, b, d, s) =>
                K.LaunchMaxMinUInt16Kernel(a, b, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        MaxMinCase(nameof(K.MaxMinUInt32Kernel), s_uint32Boundaries, s_nextUInt32,
            static (a, b, d, s) =>
                K.LaunchMaxMinUInt32Kernel(a, b, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        MaxMinCase(nameof(K.MaxMinInt64Kernel), s_int64Boundaries, s_nextInt64,
            static (a, b, d, s) =>
                K.LaunchMaxMinInt64Kernel(a, b, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        MaxMinCase(nameof(K.MaxMinUInt64Kernel), s_uint64Boundaries, s_nextUInt64,
            static (a, b, d, s) =>
                K.LaunchMaxMinUInt64Kernel(a, b, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        MaxMinCase(nameof(K.MaxMinIntPtrKernel), s_int64Boundaries, s_nextInt64,
            static (a, b, d, s) =>
                K.LaunchMaxMinIntPtrKernel(a, b, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        MaxMinCase(nameof(K.MaxMinUIntPtrKernel), s_uint64Boundaries, s_nextUInt64,
            static (a, b, d, s) =>
                K.LaunchMaxMinUIntPtrKernel(a, b, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
    ];

    // Clamp of one type with lo <= hi on every row
    internal static readonly DifferentialCase[] s_clampCases = ClampCases(reversed: false);

    // Clamp of one type with the bounds of row 0 reversed: C# throws ArgumentException, native and CUDA do not check
    // (#718); KnownDivergences.yaml lists each of these cases
    internal static readonly DifferentialCase[] s_clampReversedCases = ClampCases(reversed: true);

    private static string KernelName(string memberName, string? suffix = null) =>
        DiffKernelNames.Of(typeof(K), memberName, suffix);

    public static TheoryData<string> MaxMinCaseNames => [.. s_maxMinCases.Select(static c => c.Name)];

    public static TheoryData<string> ClampCaseNames => [.. s_clampCases.Select(static c => c.Name)];

    public static TheoryData<string> ClampReversedCaseNames => [.. s_clampReversedCases.Select(static c => c.Name)];

    private static DifferentialCase MaxMinCase<T>(
        string kernel,
        T[] boundaries,
        Func<DiffRandom, T> next,
        Action<T[], T[], T[], LaunchShape> launch)
        where T : unmanaged =>
        new(KernelName(kernel))
        {
            Shape = LaunchShapes.Linear(MatrixRows, 32),
            Inputs = (rng, shape) =>
            {
                var (a, b) = DiffValues.Pairs(rng, shape.Rows, boundaries, next);
                return new DiffBuffers()
                    .In("a", a, readOnly: true)
                    .In("b", b, readOnly: true)
                    .Out<T>("dst", shape.Rows, K.MaxMinColumns, OutputPolicy.Exact);
            },
            Launch = (b, s) => launch(b.Get<T>("a"), b.Get<T>("b"), b.Get<T>("dst"), s),
            OracleIsLaunch = true,
        };

    private static DifferentialCase[] ClampCases(bool reversed) =>
    [
        ClampCase(nameof(K.ClampSByteKernel), reversed, s_sbyteBoundaries, s_nextSByte, s_sbyteClampRows,
            static (v, lo, hi, d, s) =>
                K.LaunchClampSByteKernel(v, lo, hi, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        ClampCase(nameof(K.ClampByteKernel), reversed, s_byteBoundaries, s_nextByte, s_byteClampRows,
            static (v, lo, hi, d, s) =>
                K.LaunchClampByteKernel(v, lo, hi, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        ClampCase(nameof(K.ClampInt16Kernel), reversed, s_int16Boundaries, s_nextInt16, s_int16ClampRows,
            static (v, lo, hi, d, s) =>
                K.LaunchClampInt16Kernel(v, lo, hi, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        ClampCase(nameof(K.ClampUInt16Kernel), reversed, s_uint16Boundaries, s_nextUInt16, s_uint16ClampRows,
            static (v, lo, hi, d, s) =>
                K.LaunchClampUInt16Kernel(v, lo, hi, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        ClampCase(nameof(K.ClampUInt32Kernel), reversed, s_uint32Boundaries, s_nextUInt32, s_uint32ClampRows,
            static (v, lo, hi, d, s) =>
                K.LaunchClampUInt32Kernel(v, lo, hi, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        ClampCase(nameof(K.ClampInt64Kernel), reversed, s_int64Boundaries, s_nextInt64, s_int64ClampRows,
            static (v, lo, hi, d, s) =>
                K.LaunchClampInt64Kernel(v, lo, hi, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        ClampCase(nameof(K.ClampUInt64Kernel), reversed, s_uint64Boundaries, s_nextUInt64, s_uint64ClampRows,
            static (v, lo, hi, d, s) =>
                K.LaunchClampUInt64Kernel(v, lo, hi, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        ClampCase(nameof(K.ClampIntPtrKernel), reversed, s_int64Boundaries, s_nextInt64, s_int64ClampRows,
            static (v, lo, hi, d, s) =>
                K.LaunchClampIntPtrKernel(v, lo, hi, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
        ClampCase(nameof(K.ClampUIntPtrKernel), reversed, s_uint64Boundaries, s_nextUInt64, s_uint64ClampRows,
            static (v, lo, hi, d, s) =>
                K.LaunchClampUIntPtrKernel(v, lo, hi, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX)),
    ];

    // The value case runs the default seeds; the reversed case runs seed 0 with the bounds of row 0 swapped
    private static DifferentialCase ClampCase<T>(
        string kernel,
        bool reversed,
        T[] boundaries,
        Func<DiffRandom, T> next,
        (T Value, T Lo, T Hi)[] rows,
        Action<T[], T[], T[], T[], LaunchShape> launch)
        where T : unmanaged =>
        new(KernelName(kernel, reversed ? ReversedBounds : null))
        {
            Shape = LaunchShapes.Linear(MatrixRows, 32),
            Seeds = reversed ? [0] : null,
            OracleExceptions = reversed ? OracleExceptionMode.MatchOracle : OracleExceptionMode.None,
            Inputs = (rng, shape) =>
            {
                var (value, low, high) = BuildClampTriples(rng, shape.Rows, boundaries, next, rows);
                if (reversed)
                {
                    (low[0], high[0]) = (high[0], low[0]);
                }

                return new DiffBuffers()
                    .In("value", value, readOnly: true)
                    .In("min", low, readOnly: true)
                    .In("max", high, readOnly: true)
                    .Out<T>("dst", shape.Rows, ["clamp"], OutputPolicy.Exact);
            },
            Launch = (b, s) => launch(b.Get<T>("value"), b.Get<T>("min"), b.Get<T>("max"), b.Get<T>("dst"), s),
            OracleIsLaunch = true,
        };

    internal static readonly DifferentialCase s_integerSign =
        new(KernelName(nameof(K.IntegerSignKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var sbV = DiffValues.Values(rng, rows, s_sbyteBoundaries, s_nextSByte);
            var sV = DiffValues.Values(rng, rows, s_int16Boundaries, s_nextInt16);
            var iV = DiffValues.Values(rng, rows, s_int32Boundaries, static r => r.NextInt32());
            var lV = DiffValues.Values(rng, rows, s_int64Boundaries, s_nextInt64);
            return new DiffBuffers()
                .In("sbV", sbV, readOnly: true)
                .In("sV", sV, readOnly: true)
                .In("iV", iV, readOnly: true)
                .In("lV", lV, readOnly: true)
                .Out<int>("dst", rows, K.SignColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => K.LaunchIntegerSignKernel(
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
        new(KernelName(nameof(K.MixedOperandKernel)))
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
            .Out<long>("r", shape.Rows, K.MixedOperandColumns, OutputPolicy.Exact),
        Launch = static (b, s) => K.LaunchMixedOperandKernel(
            b.Get<long>("l"),
            b.Get<byte>("b"),
            b.Get<ushort>("us"),
            b.Get<sbyte>("sb"),
            b.Get<uint>("u"),
            b.Get<int>("iv"),
            b.Get<long>("r"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_narrowResult =
        new(KernelName(nameof(K.NarrowResultKernel)))
    {
        Shape = LaunchShapes.Linear(128, 32),
        Seeds = [0],
        Inputs = static (_, shape) => new DiffBuffers()
            .In("b", Cycle(s_mixedByte, shape.Rows), readOnly: true)
            .In("sb", Cycle(s_mixedSByte, shape.Rows), readOnly: true)
            .In("us", Cycle(s_mixedUInt16, shape.Rows), readOnly: true)
            .In("s", Cycle(s_mixedInt16, shape.Rows), readOnly: true)
            .Out<long>("r", shape.Rows, K.NarrowResultColumns, OutputPolicy.Exact),
        Launch = static (b, s) => K.LaunchNarrowResultKernel(
            b.Get<byte>("b"),
            b.Get<sbyte>("sb"),
            b.Get<ushort>("us"),
            b.Get<short>("s"),
            b.Get<long>("r"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_narrowArgument =
        new(KernelName(nameof(K.NarrowArgumentKernel)))
    {
        Shape = LaunchShapes.Linear(128, 32),
        Seeds = [0],
        Inputs = static (_, shape) => new DiffBuffers()
            .In("b", Cycle(s_mixedByte, shape.Rows), readOnly: true)
            .In("sb", Cycle(s_mixedSByte, shape.Rows), readOnly: true)
            .In("us", Cycle(s_mixedUInt16, shape.Rows), readOnly: true)
            .Out<long>("r", shape.Rows, K.NarrowArgumentColumns, OutputPolicy.Exact),
        Launch = static (b, s) => K.LaunchNarrowArgumentKernel(
            b.Get<byte>("b"),
            b.Get<sbyte>("sb"),
            b.Get<ushort>("us"),
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
        Func<DiffRandom, T> next,
        (T Value, T Lo, T Hi)[] witnesses)
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

        return (values, low, high);
    }

    // Math.Max and Math.Min of one integer type other than int match .NET on every pair of its boundaries.
    // mutant: Prelude/IntegerSemantics.hpp `__gsharp_max_int` `b > a ? b : a` -> `a > b ? b : a`
    // mutant: Prelude/IntegerSemantics.hpp `__gsharp_min_int` `b < a ? b : a` -> `a < b ? b : a`
    // input: seed 0 cross product of the boundary table of the type (MinValue against MaxValue among them)
    [SkippableTheory]
    [MemberData(nameof(MaxMinCaseNames))]
    public void IntegerMath_MaxMinOfEachType_MatchesTheOracleOnEveryPath(string caseName)
    {
        Assert.True(MatrixRows >= s_int64Boundaries.Length * s_int64Boundaries.Length);
        DifferentialRunner.Run(s_maxMinCases.Single(c => c.Name == caseName));
    }

    // Math.Clamp of one integer type other than int with lo <= hi matches .NET on all three branches.
    // mutant: Prelude.cu `__gsharp_clamp` template `value < min_val ? min_val` -> `value < min_val ? max_val`
    // input: rows 0..6 (value below, above, inside, lo == hi on both sides, value == lo, value == hi)
    [SkippableTheory]
    [MemberData(nameof(ClampCaseNames))]
    public void IntegerMath_ClampOfEachType_MatchesTheOracleOnEveryPath(string caseName) =>
        DifferentialRunner.Run(s_clampCases.Single(c => c.Name == caseName));

    // Math.Clamp with min > max throws ArgumentException in C#; native and CUDA return a value (#718, a known
    // divergence of exactly these cases, which fails as stale once a path checks it)
    // input: seed 0 with the bounds of row 0 swapped
    [SkippableTheory]
    [MemberData(nameof(ClampReversedCaseNames))]
    public void IntegerMath_ClampWithReversedBounds_MatchesTheOracleOrIsAKnownDivergence(string caseName) =>
        DifferentialRunner.Run(s_clampReversedCases.Single(c => c.Name == caseName));

    // Math.Sign for signed integer types (sbyte, short, int, long, nint) returns -1, 0, or 1 matching .NET.
    // mutant: Prelude/IntegerSemantics.hpp `__gsharp_sign_int` remove `- (int)(a < 0)`
    // input: negative boundary values (-1, -2, MinValue) across all five signed types
    [SkippableFact]
    public void IntegerMath_Sign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_integerSign);

    // Mixed operands are converted to the parameter type C# chose, so the templates deduce one type.
    // mutant: Prelude/IntegerSemantics.hpp `__gsharp_min_int` `b < a ? b : a` -> `a < b ? b : a` (minNintLit)
    // input: the cycled tables above, 128 rows
    [SkippableFact]
    public void IntegerMath_MixedOperands_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mixedOperand);

    // A narrow result kept in var keeps its type for ++ and for ref.
    // mutant: StandardCpp.yml `System.Math.Max(byte, byte)` -> "max" (CUDA's max gives an int: the ref row fails
    // to build)
    // input: the cycled tables above, 128 rows
    [SkippableFact]
    public void IntegerMath_NarrowResults_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_narrowResult);

    // A narrow argument of BitOperations, Math.Sign and Math.Abs binds to the wider overload C# picks.
    // mutant: Prelude.cu `__gsharp_round_up_pow2_32` `value - 1u` -> `value` (roundUpUshort)
    // input: the cycled tables above, 128 rows
    [SkippableFact]
    public void IntegerMath_NarrowArguments_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_narrowArgument);
}
