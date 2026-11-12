#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.IntegerTrapAndCheckedDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for integer division by zero trap semantics across signed and unsigned
/// 32-bit, 64-bit, 16-bit, and 8-bit integer operations (#718).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.IntegerTrapAndCheckedDifferential)]
public class IntegerDivisionTrapDifferentialTests
{
    internal static readonly DifferentialCase s_int32Divide = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.Int32DivideKernel)))
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

    internal static readonly DifferentialCase s_int32Remainder = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.Int32RemainderKernel)))
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

    internal static readonly DifferentialCase s_uint32Divide = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.UInt32DivideKernel)))
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

    internal static readonly DifferentialCase s_uint32Remainder = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.UInt32RemainderKernel)))
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

    internal static readonly DifferentialCase s_int64Divide = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.Int64DivideKernel)))
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
            long[] nonZeroDenoms =
            [
                1L, -1L, 2L, -2L, 3L, -7L, 16L, 31L, 127L, -128L, long.MaxValue, long.MinValue,
            ];
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

    internal static readonly DifferentialCase s_uint64Divide = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.UInt64DivideKernel)))
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

    internal static readonly DifferentialCase s_int16CompoundDivide = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.Int16CompoundDivideKernel)))
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

    internal static readonly DifferentialCase s_sbyteCompoundRemainder = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.SByteCompoundRemainderKernel)))
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

    [SkippableFact]
    public void IntegerDivision_Int32_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_int32Divide);

    [SkippableFact]
    public void IntegerDivision_Int32Remainder_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_int32Remainder);

    [SkippableFact]
    public void IntegerDivision_UInt32_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_uint32Divide);

    [SkippableFact]
    public void IntegerDivision_UInt32Remainder_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_uint32Remainder);

    [SkippableFact]
    public void IntegerDivision_Int64_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_int64Divide);

    [SkippableFact]
    public void IntegerDivision_UInt64_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_uint64Divide);

    [SkippableFact]
    public void IntegerDivision_Int16CompoundDivide_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_int16CompoundDivide);

    [SkippableFact]
    public void IntegerDivision_SByteCompoundRemainder_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_sbyteCompoundRemainder);
}
