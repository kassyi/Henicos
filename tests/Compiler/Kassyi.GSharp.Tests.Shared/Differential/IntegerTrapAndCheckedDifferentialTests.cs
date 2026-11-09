#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for integer division/remainder by zero (<c>/</c>, <c>%</c>, <c>/=</c>, <c>%=</c>),
/// <c>int.MinValue / -1</c> and <c>long.MinValue / -1L</c> overflow vs narrow <c>short.MinValue / (short)-1</c> promotion (#718),
/// <c>Math.Abs</c> on signed min values and floats (#947), shift-count masking across negative and &gt;= width counts (#722), and
/// <c>unchecked</c> arithmetic wrap-around, IEEE 754 float division by zero, and float-to-integer conversion (#724, #890).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class IntegerTrapAndCheckedDifferentialTests
{
    private const int DefaultRows = 64;

    private static readonly long[] s_int64Boundaries =
    [
        0L, 1L, -1L, 2L, -2L, long.MaxValue, long.MinValue, long.MaxValue - 1L, long.MinValue + 1L,
        0x7FFF_FFFFL, -0x8000_0000L, 0x1_0000_0000L, -0x1_0000_0000L,
    ];

    private static readonly ulong[] s_uint64Boundaries =
    [
        0UL, 1UL, 2UL, 0x7FFF_FFFFUL, 0x8000_0000UL, 0xFFFF_FFFFUL, 0x1_0000_0000UL,
        ulong.MaxValue - 1UL, ulong.MaxValue,
    ];

    private static readonly double[] s_doubleBoundaries =
    [
        0.0, -0.0, 1.0, -1.0, 0.5, -0.5, double.MaxValue, double.MinValue,
        double.PositiveInfinity, double.NegativeInfinity, double.NaN,
        2147483647.0, 2147483648.0, -2147483648.0, -2147483649.0,
        4294967295.0, 4294967296.0, 9.223372036854776e18, -9.223372036854776e18,
    ];

    private static readonly DifferentialCase s_int32Divide = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int32DivideKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (x86 idiv by 0 terminates process on native)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            int[] nonZeroDenoms = [1, -1, 2, -2, 3, -7, 16, 31, 127, -128, int.MaxValue, int.MinValue];
            var nums = DiffValues.Int32(rng, rows);
            var denoms = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                int d = nonZeroDenoms[k % nonZeroDenoms.Length];
                if (nums[k] == int.MinValue && d == -1)
                {
                    d = 1;
                }

                denoms[k] = d;
            }

            if (rng.Seed == 1)
            {
                denoms[5] = 0;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<int>("dst", rows, ["i_div"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchInt32DivideKernel(
            b.Get<int>("nums"),
            b.Get<int>("denoms"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.Int32DivideKernel(
            b.Get<int>("nums"),
            b.Get<int>("denoms"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_int32Remainder = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int32RemainderKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (x86 idiv by 0 terminates process on native)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            int[] nonZeroDenoms = [1, -1, 2, -2, 3, -7, 16, 31, 127, -128, int.MaxValue, int.MinValue];
            var nums = DiffValues.Int32(rng, rows);
            var denoms = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                int d = nonZeroDenoms[k % nonZeroDenoms.Length];
                if (nums[k] == int.MinValue && d == -1)
                {
                    d = 1;
                }

                denoms[k] = d;
            }

            if (rng.Seed == 1)
            {
                denoms[5] = 0;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<int>("dst", rows, ["i_rem"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchInt32RemainderKernel(
            b.Get<int>("nums"),
            b.Get<int>("denoms"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.Int32RemainderKernel(
            b.Get<int>("nums"),
            b.Get<int>("denoms"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_uint32Divide = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.UInt32DivideKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (x86 div by 0 terminates process on native)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            uint[] nonZeroDenoms = [1u, 2u, 3u, 7u, 16u, 255u, 65535u, uint.MaxValue];
            var nums = new uint[rows];
            var denoms = new uint[rows];
            for (int k = 0; k < rows; k++)
            {
                nums[k] = (uint)rng.NextInt32();
                denoms[k] = nonZeroDenoms[k % nonZeroDenoms.Length];
            }

            if (rng.Seed == 1)
            {
                denoms[5] = 0u;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<uint>("dst", rows, ["u_div"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchUInt32DivideKernel(
            b.Get<uint>("nums"),
            b.Get<uint>("denoms"),
            b.Get<uint>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.UInt32DivideKernel(
            b.Get<uint>("nums"),
            b.Get<uint>("denoms"),
            b.Get<uint>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_uint32Remainder = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.UInt32RemainderKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (x86 div by 0 terminates process on native)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            uint[] nonZeroDenoms = [1u, 2u, 3u, 7u, 16u, 255u, 65535u, uint.MaxValue];
            var nums = new uint[rows];
            var denoms = new uint[rows];
            for (int k = 0; k < rows; k++)
            {
                nums[k] = (uint)rng.NextInt32();
                denoms[k] = nonZeroDenoms[k % nonZeroDenoms.Length];
            }

            if (rng.Seed == 1)
            {
                denoms[5] = 0u;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<uint>("dst", rows, ["u_rem"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchUInt32RemainderKernel(
            b.Get<uint>("nums"),
            b.Get<uint>("denoms"),
            b.Get<uint>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.UInt32RemainderKernel(
            b.Get<uint>("nums"),
            b.Get<uint>("denoms"),
            b.Get<uint>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_int64Divide = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int64DivideKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (x86 idiv by 0 terminates process on native)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            long[] nonZeroDenoms = [1L, -1L, 2L, -2L, 3L, -7L, 16L, 31L, 127L, -128L, long.MaxValue, long.MinValue];
            var nums = new long[rows];
            var denoms = new long[rows];
            for (int k = 0; k < rows; k++)
            {
                nums[k] = s_int64Boundaries[k % s_int64Boundaries.Length];
                long d = nonZeroDenoms[k % nonZeroDenoms.Length];
                if (nums[k] == long.MinValue && d == -1L)
                {
                    d = 1L;
                }

                denoms[k] = d;
            }

            if (rng.Seed == 1)
            {
                denoms[5] = 0L;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<long>("dst", rows, ["l_div"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchInt64DivideKernel(
            b.Get<long>("nums"),
            b.Get<long>("denoms"),
            b.Get<long>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.Int64DivideKernel(
            b.Get<long>("nums"),
            b.Get<long>("denoms"),
            b.Get<long>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_uint64Divide = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.UInt64DivideKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (x86 div by 0 terminates process on native)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            ulong[] nonZeroDenoms = [1UL, 2UL, 3UL, 7UL, 16UL, 255UL, 65535UL, ulong.MaxValue];
            var nums = new ulong[rows];
            var denoms = new ulong[rows];
            for (int k = 0; k < rows; k++)
            {
                nums[k] = s_uint64Boundaries[k % s_uint64Boundaries.Length];
                denoms[k] = nonZeroDenoms[k % nonZeroDenoms.Length];
            }

            if (rng.Seed == 1)
            {
                denoms[5] = 0UL;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<ulong>("dst", rows, ["ul_div"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchUInt64DivideKernel(
            b.Get<ulong>("nums"),
            b.Get<ulong>("denoms"),
            b.Get<ulong>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.UInt64DivideKernel(
            b.Get<ulong>("nums"),
            b.Get<ulong>("denoms"),
            b.Get<ulong>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_int16CompoundDivide = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int16CompoundDivideKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (x86 idiv by 0 terminates process on native)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            short[] nonZeroDenoms = [1, -1, 2, -2, 7, -13, 127, -127];
            var nums = DiffValues.Int16(rng, rows);
            var denoms = new short[rows];
            for (int k = 0; k < rows; k++)
            {
                short d = nonZeroDenoms[k % nonZeroDenoms.Length];
                if (nums[k] == short.MinValue && d == -1)
                {
                    d = 1;
                }

                denoms[k] = d;
            }

            if (rng.Seed == 1)
            {
                denoms[5] = 0;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<short>("dst", rows, ["s_compound_div"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchInt16CompoundDivideKernel(
            b.Get<short>("nums"),
            b.Get<short>("denoms"),
            b.Get<short>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.Int16CompoundDivideKernel(
            b.Get<short>("nums"),
            b.Get<short>("denoms"),
            b.Get<short>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_sbyteCompoundRemainder = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.SByteCompoundRemainderKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (x86 idiv by 0 terminates process on native)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            sbyte[] nonZeroDenoms = [1, -1, 2, -2, 7, -13, 127, -127];
            var nums = DiffValues.SByte(rng, rows);
            var denoms = new sbyte[rows];
            for (int k = 0; k < rows; k++)
            {
                sbyte d = nonZeroDenoms[k % nonZeroDenoms.Length];
                if (nums[k] == sbyte.MinValue && d == -1)
                {
                    d = 1;
                }

                denoms[k] = d;
            }

            if (rng.Seed == 1)
            {
                denoms[5] = 0;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<sbyte>("dst", rows, ["sb_compound_rem"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchSByteCompoundRemainderKernel(
            b.Get<sbyte>("nums"),
            b.Get<sbyte>("denoms"),
            b.Get<sbyte>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.SByteCompoundRemainderKernel(
            b.Get<sbyte>("nums"),
            b.Get<sbyte>("denoms"),
            b.Get<sbyte>("dst"),
            s.Rows),
    };

    // Signed MinValue / -1: #718 gives GPU result (quotient MinValue, remainder 0) with no exception.
    // Managed throws OverflowException (covered by KnownDivergences #718).
    // Native is skipped (#718) because x86 idiv terminates the process.
    private static readonly DifferentialCase s_int32MinValueDivide = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int32MinValueDivideKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (x86 idiv with int.MinValue / -1 terminates process on native)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            (int Num, int Den)[] safePairs =
            [
                (int.MinValue, 1),
                (int.MinValue, 2),
                (int.MinValue + 1, -1),
                (int.MaxValue, -1),
                (-1, -1),
                (0, -1),
            ];

            var nums = new int[rows];
            var denoms = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                var p = safePairs[k % safePairs.Length];
                nums[k] = p.Num;
                denoms[k] = p.Den;
            }

            if (rng.Seed == 1)
            {
                nums[3] = int.MinValue;
                denoms[3] = -1;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<int>("dst", rows, ["i_quot"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchInt32MinValueDivideKernel(
            b.Get<int>("nums"),
            b.Get<int>("denoms"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, s) =>
        {
            var nums = b.Get<int>("nums");
            var denoms = b.Get<int>("denoms");
            var dst = b.Get<int>("dst");
            for (int t = 0; t < s.Rows; t++)
            {
                dst[t] = nums[t] == int.MinValue && denoms[t] == -1 ? int.MinValue : nums[t] / denoms[t];
            }
        },
    };

    private static readonly DifferentialCase s_int32MinValueRemainder = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int32MinValueRemainderKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (x86 idiv with int.MinValue % -1 terminates process on native)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            (int Num, int Den)[] safePairs =
            [
                (int.MinValue, 1),
                (int.MinValue, 2),
                (int.MinValue + 1, -1),
                (int.MaxValue, -1),
                (-1, -1),
                (0, -1),
            ];

            var nums = new int[rows];
            var denoms = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                var p = safePairs[k % safePairs.Length];
                nums[k] = p.Num;
                denoms[k] = p.Den;
            }

            if (rng.Seed == 1)
            {
                nums[3] = int.MinValue;
                denoms[3] = -1;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<int>("dst", rows, ["i_rem"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchInt32MinValueRemainderKernel(
            b.Get<int>("nums"),
            b.Get<int>("denoms"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, s) =>
        {
            var nums = b.Get<int>("nums");
            var denoms = b.Get<int>("denoms");
            var dst = b.Get<int>("dst");
            for (int t = 0; t < s.Rows; t++)
            {
                dst[t] = nums[t] == int.MinValue && denoms[t] == -1 ? 0 : nums[t] % denoms[t];
            }
        },
    };

    private static readonly DifferentialCase s_int64MinValueDivide = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int64MinValueDivideKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (x86 idiv with long.MinValue / -1L terminates process on native)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            (long Num, long Den)[] safePairs =
            [
                (long.MinValue, 1L),
                (long.MinValue, 2L),
                (long.MinValue + 1L, -1L),
                (long.MaxValue, -1L),
                (-1L, -1L),
                (0L, -1L),
            ];

            var nums = new long[rows];
            var denoms = new long[rows];
            for (int k = 0; k < rows; k++)
            {
                var p = safePairs[k % safePairs.Length];
                nums[k] = p.Num;
                denoms[k] = p.Den;
            }

            if (rng.Seed == 1)
            {
                nums[3] = long.MinValue;
                denoms[3] = -1L;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<long>("dst", rows, ["l_quot"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchInt64MinValueDivideKernel(
            b.Get<long>("nums"),
            b.Get<long>("denoms"),
            b.Get<long>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, s) =>
        {
            var nums = b.Get<long>("nums");
            var denoms = b.Get<long>("denoms");
            var dst = b.Get<long>("dst");
            for (int t = 0; t < s.Rows; t++)
            {
                dst[t] = nums[t] == long.MinValue && denoms[t] == -1L ? long.MinValue : nums[t] / denoms[t];
            }
        },
    };

    private static readonly DifferentialCase s_int64MinValueRemainder = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int64MinValueRemainderKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (x86 idiv with long.MinValue % -1L terminates process on native)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            (long Num, long Den)[] safePairs =
            [
                (long.MinValue, 1L),
                (long.MinValue, 2L),
                (long.MinValue + 1L, -1L),
                (long.MaxValue, -1L),
                (-1L, -1L),
                (0L, -1L),
            ];

            var nums = new long[rows];
            var denoms = new long[rows];
            for (int k = 0; k < rows; k++)
            {
                var p = safePairs[k % safePairs.Length];
                nums[k] = p.Num;
                denoms[k] = p.Den;
            }

            if (rng.Seed == 1)
            {
                nums[3] = long.MinValue;
                denoms[3] = -1L;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<long>("dst", rows, ["l_rem"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchInt64MinValueRemainderKernel(
            b.Get<long>("nums"),
            b.Get<long>("denoms"),
            b.Get<long>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, s) =>
        {
            var nums = b.Get<long>("nums");
            var denoms = b.Get<long>("denoms");
            var dst = b.Get<long>("dst");
            for (int t = 0; t < s.Rows; t++)
            {
                dst[t] = nums[t] == long.MinValue && denoms[t] == -1L ? 0L : nums[t] % denoms[t];
            }
        },
    };

    private static readonly DifferentialCase s_shortMinValueDivide = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.ShortMinValueDivideKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Inputs = static (_, shape) =>
        {
            int rows = shape.Rows;
            var nums = new short[rows];
            var denoms = new short[rows];
            for (int k = 0; k < rows; k++)
            {
                nums[k] = short.MinValue;
                denoms[k] = -1;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<int>("dst", rows, ["s_min_div"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchShortMinValueDivideKernel(
            b.Get<short>("nums"),
            b.Get<short>("denoms"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.ShortMinValueDivideKernel(
            b.Get<short>("nums"),
            b.Get<short>("denoms"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_sbyteMinValueDivide = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.SByteMinValueDivideKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Inputs = static (_, shape) =>
        {
            int rows = shape.Rows;
            var nums = new sbyte[rows];
            var denoms = new sbyte[rows];
            for (int k = 0; k < rows; k++)
            {
                nums[k] = sbyte.MinValue;
                denoms[k] = -1;
            }

            return new DiffBuffers()
                .In("nums", nums, readOnly: true)
                .In("denoms", denoms, readOnly: true)
                .Out<int>("dst", rows, ["sb_min_div"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchSByteMinValueDivideKernel(
            b.Get<sbyte>("nums"),
            b.Get<sbyte>("denoms"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.SByteMinValueDivideKernel(
            b.Get<sbyte>("nums"),
            b.Get<sbyte>("denoms"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_mathAbsInt32 = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.MathAbsInt32Kernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            int[] safeVals = [0, 1, -1, 2, -2, int.MaxValue, int.MinValue + 1, -32768, -128];
            var vals = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                vals[k] = safeVals[k % safeVals.Length];
            }

            if (rng.Seed == 1)
            {
                vals[7] = int.MinValue;
            }

            return new DiffBuffers()
                .In("vals", vals, readOnly: true)
                .Out<int>("dst", rows, ["abs_int32"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchMathAbsInt32Kernel(
            b.Get<int>("vals"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.MathAbsInt32Kernel(
            b.Get<int>("vals"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_mathAbsSingle = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.MathAbsSingleKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var vals = DiffValues.Single(rng, rows);
            vals[0] = 0.0f;
            vals[1] = -0.0f;
            vals[2] = float.PositiveInfinity;
            vals[3] = float.NegativeInfinity;
            vals[4] = float.NaN;
            return new DiffBuffers()
                .In("vals", vals, readOnly: true)
                .Out<float>("dst", rows, ["abs_single"], OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchMathAbsSingleKernel(
            b.Get<float>("vals"),
            b.Get<float>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.MathAbsSingleKernel(
            b.Get<float>("vals"),
            b.Get<float>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_mathAbsDouble = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.MathAbsDoubleKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Inputs = static (_, shape) =>
        {
            int rows = shape.Rows;
            var vals = new double[rows];
            for (int k = 0; k < rows; k++)
            {
                vals[k] = s_doubleBoundaries[k % s_doubleBoundaries.Length];
            }

            return new DiffBuffers()
                .In("vals", vals, readOnly: true)
                .Out<double>("dst", rows, ["abs_double"], OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchMathAbsDoubleKernel(
            b.Get<double>("vals"),
            b.Get<double>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.MathAbsDoubleKernel(
            b.Get<double>("vals"),
            b.Get<double>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_shiftCountMaskBoundaryMatrix = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.ShiftCountMaskBoundaryMatrixKernel)}")
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        ExtraShapesSeed0Only =
        [
            LaunchShapes.Linear(64, 32),
        ],
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            int[] shiftCounts = [-1, -31, -32, -63, -64, 0, 1, 7, 15, 16, 31, 32, 33, 63, 64, 65, int.MinValue, int.MaxValue];
            var iVals = DiffValues.Int32(rng, rows);
            var uVals = new uint[rows];
            var lVals = new long[rows];
            var ulVals = new ulong[rows];
            var sVals = DiffValues.Int16(rng, rows);
            var sbVals = DiffValues.SByte(rng, rows);
            var counts = new int[rows];

            for (int k = 0; k < rows; k++)
            {
                uVals[k] = (uint)iVals[k];
                lVals[k] = rng.IsBoundarySeed
                    ? s_int64Boundaries[k % s_int64Boundaries.Length]
                    : ((long)rng.NextInt32() << 32) | rng.NextUInt32();
                ulVals[k] = rng.IsBoundarySeed
                    ? s_uint64Boundaries[k % s_uint64Boundaries.Length]
                    : (ulong)lVals[k];
                counts[k] = rng.IsBoundarySeed
                    ? shiftCounts[k % shiftCounts.Length]
                    : (rng.NextBool() ? shiftCounts[rng.NextBelow(shiftCounts.Length)] : rng.NextInt32());
            }

            return new DiffBuffers()
                .In("iVals", iVals, readOnly: true)
                .In("uVals", uVals, readOnly: true)
                .In("lVals", lVals, readOnly: true)
                .In("ulVals", ulVals, readOnly: true)
                .In("sVals", sVals, readOnly: true)
                .In("sbVals", sbVals, readOnly: true)
                .In("counts", counts, readOnly: true)
                .Out<int>("dst", rows, IntegerTrapAndCheckedDifferentialKernels.ShiftMaskBoundaryColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchShiftCountMaskBoundaryMatrixKernel(
            b.Get<int>("iVals"),
            b.Get<uint>("uVals"),
            b.Get<long>("lVals"),
            b.Get<ulong>("ulVals"),
            b.Get<short>("sVals"),
            b.Get<sbyte>("sbVals"),
            b.Get<int>("counts"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.ShiftCountMaskBoundaryMatrixKernel(
            b.Get<int>("iVals"),
            b.Get<uint>("uVals"),
            b.Get<long>("lVals"),
            b.Get<ulong>("ulVals"),
            b.Get<short>("sVals"),
            b.Get<sbyte>("sbVals"),
            b.Get<int>("counts"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_uncheckedIntegerWrap = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.UncheckedIntegerWrapKernel)}")
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var (left, right) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("left", left, readOnly: true)
                .In("right", right, readOnly: true)
                .Out<int>("dst", rows, IntegerTrapAndCheckedDifferentialKernels.UncheckedIntegerWrapColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchUncheckedIntegerWrapKernel(
            b.Get<int>("left"),
            b.Get<int>("right"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.UncheckedIntegerWrapKernel(
            b.Get<int>("left"),
            b.Get<int>("right"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_floatDivisionByZero = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.FloatDivisionByZeroKernel)}")
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var fVals = DiffValues.Single(rng, rows);
            var dVals = new double[rows];
            for (int k = 0; k < rows; k++)
            {
                dVals[k] = s_doubleBoundaries[k % s_doubleBoundaries.Length];
            }

            return new DiffBuffers()
                .In("fVals", fVals, readOnly: true)
                .In("dVals", dVals, readOnly: true)
                .Out<float>("fDst", rows, ["f_div_zero", "f_rem_zero"], OutputPolicy.Bitwise)
                .Out<double>("dDst", rows, ["d_div_zero", "d_rem_zero"], OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchFloatDivisionByZeroKernel(
            b.Get<float>("fVals"),
            b.Get<double>("dVals"),
            b.Get<float>("fDst"),
            b.Get<double>("dDst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.FloatDivisionByZeroKernel(
            b.Get<float>("fVals"),
            b.Get<double>("dVals"),
            b.Get<float>("fDst"),
            b.Get<double>("dDst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_floatToIntegerConversion = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.FloatToIntegerConversionKernel)}")
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var fVals = DiffValues.Single(rng, rows);
            var dVals = new double[rows];
            for (int k = 0; k < rows; k++)
            {
                dVals[k] = s_doubleBoundaries[k % s_doubleBoundaries.Length];
            }

            return new DiffBuffers()
                .In("fVals", fVals, readOnly: true)
                .In("dVals", dVals, readOnly: true)
                .Out<int>("dst", rows, IntegerTrapAndCheckedDifferentialKernels.FloatToIntegerConversionColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchFloatToIntegerConversionKernel(
            b.Get<float>("fVals"),
            b.Get<double>("dVals"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.FloatToIntegerConversionKernel(
            b.Get<float>("fVals"),
            b.Get<double>("dVals"),
            b.Get<int>("dst"),
            s.Rows),
    };

    [SkippableFact]
    public void Test_Int32Divide_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_int32Divide);

    [SkippableFact]
    public void Test_Int32Remainder_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_int32Remainder);

    [SkippableFact]
    public void Test_UInt32Divide_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_uint32Divide);

    [SkippableFact]
    public void Test_UInt32Remainder_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_uint32Remainder);

    [SkippableFact]
    public void Test_Int64Divide_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_int64Divide);

    [SkippableFact]
    public void Test_UInt64Divide_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_uint64Divide);

    [SkippableFact]
    public void Test_Int16CompoundDivide_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_int16CompoundDivide);

    [SkippableFact]
    public void Test_SByteCompoundRemainder_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_sbyteCompoundRemainder);

    [SkippableFact]
    public void Test_Int32MinValueDivide_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_int32MinValueDivide);

    [SkippableFact]
    public void Test_Int32MinValueRemainder_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_int32MinValueRemainder);

    [SkippableFact]
    public void Test_Int64MinValueDivide_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_int64MinValueDivide);

    [SkippableFact]
    public void Test_Int64MinValueRemainder_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_int64MinValueRemainder);

    [SkippableFact]
    public void Test_ShortMinValueDivide_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_shortMinValueDivide);

    [SkippableFact]
    public void Test_SByteMinValueDivide_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_sbyteMinValueDivide);

    [SkippableFact]
    public void Test_MathAbsInt32_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathAbsInt32);

    [SkippableFact]
    public void Test_MathAbsSingle_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathAbsSingle);

    [SkippableFact]
    public void Test_MathAbsDouble_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathAbsDouble);

    [SkippableFact]
    public void Test_ShiftCountMaskBoundaryMatrix_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_shiftCountMaskBoundaryMatrix);

    [SkippableFact]
    public void Test_UncheckedIntegerWrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_uncheckedIntegerWrap);

    [SkippableFact]
    public void Test_FloatDivisionByZero_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_floatDivisionByZero);

    [SkippableFact]
    public void Test_FloatToIntegerConversion_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_floatToIntegerConversion);
}
