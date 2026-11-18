#nullable enable
using System;
using System.Numerics;
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
    internal static readonly DifferentialCase s_int32MinValueDivide = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.Int32MinValueDivideKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
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

    internal static readonly DifferentialCase s_int32MinValueRemainder = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.Int32MinValueRemainderKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
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

    internal static readonly DifferentialCase s_int64MinValueDivide = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.Int64MinValueDivideKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
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

    internal static readonly DifferentialCase s_int64MinValueRemainder = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.Int64MinValueRemainderKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
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

    internal static readonly DifferentialCase s_shortMinValueDivide = new(
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
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_sbyteMinValueDivide = new(
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
        OracleIsLaunch = true,
    };

    [SkippableFact]
    public void IntegerOverflow_Int32MinValueDivide_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_int32MinValueDivide);

    [SkippableFact]
    public void IntegerOverflow_Int32MinValueRemainder_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_int32MinValueRemainder);

    [SkippableFact]
    public void IntegerOverflow_Int64MinValueDivide_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_int64MinValueDivide);

    [SkippableFact]
    public void IntegerOverflow_Int64MinValueRemainder_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_int64MinValueRemainder);

    [SkippableFact]
    public void IntegerOverflow_ShortMinValueDivide_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_shortMinValueDivide);

    [SkippableFact]
    public void IntegerOverflow_SByteMinValueDivide_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_sbyteMinValueDivide);

    // One kernel and one test per shape of MinValue / -1 and MinValue % -1, int and long side by side. A remainder
    // that is stored directly is folded to 0 by nvcc even without the b == -1 check; the shapes that use the
    // remainder in an expression (RemPlusFive, RemCompare, RemCallCompare) are the ones that go wrong without it:
    // with the check removed from __gsharp_rem_i32 / __gsharp_rem_i64 they give 0, 2 and 2 instead of 5, 1 and 1.
    // DivShift keeps the quotient in an expression too.

    [SkippableFact]
    public void IntegerOverflow_Int32Div_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<int>(
            nameof(IntegerMinValueConstantKernels.Int32DivKernel),
            int.MinValue,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt32DivKernel(
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int64Div_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<long>(
            nameof(IntegerMinValueConstantKernels.Int64DivKernel),
            long.MinValue,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt64DivKernel(
                b.Get<long>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int32DivAssign_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<int>(
            nameof(IntegerMinValueConstantKernels.Int32DivAssignKernel),
            int.MinValue,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt32DivAssignKernel(
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int64DivAssign_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<long>(
            nameof(IntegerMinValueConstantKernels.Int64DivAssignKernel),
            long.MinValue,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt64DivAssignKernel(
                b.Get<long>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int32Rem_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<int>(
            nameof(IntegerMinValueConstantKernels.Int32RemKernel),
            0,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt32RemKernel(
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int64Rem_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<long>(
            nameof(IntegerMinValueConstantKernels.Int64RemKernel),
            0L,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt64RemKernel(
                b.Get<long>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int32RemAssign_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<int>(
            nameof(IntegerMinValueConstantKernels.Int32RemAssignKernel),
            0,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt32RemAssignKernel(
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int64RemAssign_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<long>(
            nameof(IntegerMinValueConstantKernels.Int64RemAssignKernel),
            0L,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt64RemAssignKernel(
                b.Get<long>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int32DivCall_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<int>(
            nameof(IntegerMinValueConstantKernels.Int32DivCallKernel),
            int.MinValue,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt32DivCallKernel(
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int64DivCall_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<long>(
            nameof(IntegerMinValueConstantKernels.Int64DivCallKernel),
            long.MinValue,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt64DivCallKernel(
                b.Get<long>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int32RemCall_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<int>(
            nameof(IntegerMinValueConstantKernels.Int32RemCallKernel),
            0,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt32RemCallKernel(
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int64RemCall_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<long>(
            nameof(IntegerMinValueConstantKernels.Int64RemCallKernel),
            0L,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt64RemCallKernel(
                b.Get<long>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int32DivRuntimeDivisor_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<int>(
            nameof(IntegerMinValueConstantKernels.Int32DivRuntimeDivisorKernel),
            int.MinValue,
            true,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt32DivRuntimeDivisorKernel(
                b.Get<int>("denoms"),
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int64DivRuntimeDivisor_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<long>(
            nameof(IntegerMinValueConstantKernels.Int64DivRuntimeDivisorKernel),
            long.MinValue,
            true,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt64DivRuntimeDivisorKernel(
                b.Get<long>("denoms"),
                b.Get<long>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int32DivConstDivisor_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<int>(
            nameof(IntegerMinValueConstantKernels.Int32DivConstDivisorKernel),
            int.MinValue,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt32DivConstDivisorKernel(
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int64DivConstDivisor_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<long>(
            nameof(IntegerMinValueConstantKernels.Int64DivConstDivisorKernel),
            long.MinValue,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt64DivConstDivisorKernel(
                b.Get<long>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int32RemPlusFive_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<int>(
            nameof(IntegerMinValueConstantKernels.Int32RemPlusFiveKernel),
            5,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt32RemPlusFiveKernel(
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int64RemPlusFive_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<long>(
            nameof(IntegerMinValueConstantKernels.Int64RemPlusFiveKernel),
            5L,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt64RemPlusFiveKernel(
                b.Get<long>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int32RemCompare_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<int>(
            nameof(IntegerMinValueConstantKernels.Int32RemCompareKernel),
            1,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt32RemCompareKernel(
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int64RemCompare_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<long>(
            nameof(IntegerMinValueConstantKernels.Int64RemCompareKernel),
            1L,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt64RemCompareKernel(
                b.Get<long>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int32RemCallCompare_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<int>(
            nameof(IntegerMinValueConstantKernels.Int32RemCallCompareKernel),
            1,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt32RemCallCompareKernel(
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int64RemCallCompare_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<long>(
            nameof(IntegerMinValueConstantKernels.Int64RemCallCompareKernel),
            1L,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt64RemCallCompareKernel(
                b.Get<long>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int32DivShift_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<int>(
            nameof(IntegerMinValueConstantKernels.Int32DivShiftKernel),
            -1,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt32DivShiftKernel(
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    [SkippableFact]
    public void IntegerOverflow_Int64DivShift_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase<long>(
            nameof(IntegerMinValueConstantKernels.Int64DivShiftKernel),
            -2147483648L,
            false,
            static (b, s) => IntegerMinValueConstantKernels.LaunchInt64DivShiftKernel(
                b.Get<long>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX)));

    private static DifferentialCase ConstCase<T>(
        string kernel,
        T expected,
        bool withDenominators,
        Action<DiffBuffers, LaunchShape> launch)
        where T : unmanaged, INumberBase<T>
    {
        return new DifferentialCase(DiffKernelNames.Of(typeof(IntegerMinValueConstantKernels), kernel))
        {
            Shape = LaunchShapes.Linear(DefaultRows, 32),
            Inputs = (_, shape) =>
            {
                var buffers = new DiffBuffers();
                if (withDenominators)
                {
                    var denominators = new T[shape.Rows];
                    Array.Fill(denominators, -T.One);
                    buffers.In("denoms", denominators, readOnly: true);
                }

                return buffers.Out<T>("dst", shape.Rows, OutputPolicy.Exact);
            },
            Launch = launch,
            Reference = (b, s) => Array.Fill(b.Get<T>("dst"), expected),
        };
    }
}
