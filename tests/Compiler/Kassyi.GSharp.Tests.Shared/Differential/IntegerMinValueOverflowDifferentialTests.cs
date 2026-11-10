#nullable enable
using System.Collections.Generic;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.IntegerTrapAndCheckedDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <c>int.MinValue / -1</c> and <c>long.MinValue / -1L</c> overflow vs
/// narrow <c>short.MinValue / (short)-1</c> and <c>sbyte.MinValue / (sbyte)-1</c> promotion (#718).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.IntegerTrapAndCheckedDifferential)]
public class IntegerMinValueOverflowDifferentialTests
{
    private static readonly DifferentialCase s_int32MinValueDivide = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.Int32MinValueDivideKernel)))
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
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.Int32MinValueRemainderKernel)))
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
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.Int64MinValueDivideKernel)))
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
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.Int64MinValueRemainderKernel)))
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
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.ShortMinValueDivideKernel)))
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
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.SByteMinValueDivideKernel)))
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
}
