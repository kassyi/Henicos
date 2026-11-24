#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
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
    internal static readonly DifferentialCase s_int32MinValueDivide = DiffCases.Create(
        IntegerTrapAndCheckedDifferentialKernels.LaunchInt32MinValueDivideKernel,
        inputs: static (rng, shape) =>
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
        shape: LaunchShapes.Linear(DefaultRows, 32),
        firstInputName: "nums",
        secondInputName: "denoms",
        seeds: [0, 1],
        reference: static (b, s) =>
        {
            var nums = b.Get<int>("nums");
            var denoms = b.Get<int>("denoms");
            var dst = b.Get<int>("dst");
            for (int t = 0; t < s.Rows; t++)
            {
                dst[t] = nums[t] == int.MinValue && denoms[t] == -1 ? int.MinValue : nums[t] / denoms[t];
            }
        });

    internal static readonly DifferentialCase s_int32MinValueRemainder = DiffCases.Create(
        IntegerTrapAndCheckedDifferentialKernels.LaunchInt32MinValueRemainderKernel,
        inputs: static (rng, shape) =>
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
        shape: LaunchShapes.Linear(DefaultRows, 32),
        firstInputName: "nums",
        secondInputName: "denoms",
        seeds: [0, 1],
        reference: static (b, s) =>
        {
            var nums = b.Get<int>("nums");
            var denoms = b.Get<int>("denoms");
            var dst = b.Get<int>("dst");
            for (int t = 0; t < s.Rows; t++)
            {
                dst[t] = nums[t] == int.MinValue && denoms[t] == -1 ? 0 : nums[t] % denoms[t];
            }
        });

    internal static readonly DifferentialCase s_int64MinValueDivide = DiffCases.Create(
        IntegerTrapAndCheckedDifferentialKernels.LaunchInt64MinValueDivideKernel,
        inputs: static (rng, shape) =>
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
        shape: LaunchShapes.Linear(DefaultRows, 32),
        firstInputName: "nums",
        secondInputName: "denoms",
        seeds: [0, 1],
        reference: static (b, s) =>
        {
            var nums = b.Get<long>("nums");
            var denoms = b.Get<long>("denoms");
            var dst = b.Get<long>("dst");
            for (int t = 0; t < s.Rows; t++)
            {
                dst[t] = nums[t] == long.MinValue && denoms[t] == -1L ? long.MinValue : nums[t] / denoms[t];
            }
        });

    internal static readonly DifferentialCase s_int64MinValueRemainder = DiffCases.Create(
        IntegerTrapAndCheckedDifferentialKernels.LaunchInt64MinValueRemainderKernel,
        inputs: static (rng, shape) =>
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
        shape: LaunchShapes.Linear(DefaultRows, 32),
        firstInputName: "nums",
        secondInputName: "denoms",
        seeds: [0, 1],
        reference: static (b, s) =>
        {
            var nums = b.Get<long>("nums");
            var denoms = b.Get<long>("denoms");
            var dst = b.Get<long>("dst");
            for (int t = 0; t < s.Rows; t++)
            {
                dst[t] = nums[t] == long.MinValue && denoms[t] == -1L ? 0L : nums[t] % denoms[t];
            }
        });

    internal static readonly DifferentialCase s_shortMinValueDivide = DiffCases.CreateRows<short, short, int>(
        IntegerTrapAndCheckedDifferentialKernels.LaunchShortMinValueDivideKernel,
        inputs: static (_, shape) =>
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
        shape: LaunchShapes.Linear(DefaultRows, 32),
        firstInputName: "nums",
        secondInputName: "denoms");

    internal static readonly DifferentialCase s_sbyteMinValueDivide = DiffCases.CreateRows<sbyte, sbyte, int>(
        IntegerTrapAndCheckedDifferentialKernels.LaunchSByteMinValueDivideKernel,
        inputs: static (_, shape) =>
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
        shape: LaunchShapes.Linear(DefaultRows, 32),
        firstInputName: "nums",
        secondInputName: "denoms");

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
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt32DivKernel, int.MinValue));

    [SkippableFact]
    public void IntegerOverflow_Int64Div_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt64DivKernel, long.MinValue));

    [SkippableFact]
    public void IntegerOverflow_Int32DivAssign_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt32DivAssignKernel, int.MinValue));

    [SkippableFact]
    public void IntegerOverflow_Int64DivAssign_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt64DivAssignKernel, long.MinValue));

    [SkippableFact]
    public void IntegerOverflow_Int32Rem_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt32RemKernel, 0));

    [SkippableFact]
    public void IntegerOverflow_Int64Rem_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt64RemKernel, 0L));

    [SkippableFact]
    public void IntegerOverflow_Int32RemAssign_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt32RemAssignKernel, 0));

    [SkippableFact]
    public void IntegerOverflow_Int64RemAssign_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt64RemAssignKernel, 0L));

    [SkippableFact]
    public void IntegerOverflow_Int32DivCall_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt32DivCallKernel, int.MinValue));

    [SkippableFact]
    public void IntegerOverflow_Int64DivCall_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt64DivCallKernel, long.MinValue));

    [SkippableFact]
    public void IntegerOverflow_Int32RemCall_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt32RemCallKernel, 0));

    [SkippableFact]
    public void IntegerOverflow_Int64RemCall_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt64RemCallKernel, 0L));

    [SkippableFact]
    public void IntegerOverflow_Int32DivRuntimeDivisor_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(
            IntegerMinValueConstantKernels.LaunchInt32DivRuntimeDivisorKernel,
            int.MinValue));

    [SkippableFact]
    public void IntegerOverflow_Int64DivRuntimeDivisor_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(
            IntegerMinValueConstantKernels.LaunchInt64DivRuntimeDivisorKernel,
            long.MinValue));

    [SkippableFact]
    public void IntegerOverflow_Int32DivConstDivisor_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(
            IntegerMinValueConstantKernels.LaunchInt32DivConstDivisorKernel,
            int.MinValue));

    [SkippableFact]
    public void IntegerOverflow_Int64DivConstDivisor_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(
            IntegerMinValueConstantKernels.LaunchInt64DivConstDivisorKernel,
            long.MinValue));

    [SkippableFact]
    public void IntegerOverflow_Int32RemPlusFive_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt32RemPlusFiveKernel, 5));

    [SkippableFact]
    public void IntegerOverflow_Int64RemPlusFive_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt64RemPlusFiveKernel, 5L));

    [SkippableFact]
    public void IntegerOverflow_Int32RemCompare_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt32RemCompareKernel, 1));

    [SkippableFact]
    public void IntegerOverflow_Int64RemCompare_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt64RemCompareKernel, 1L));

    [SkippableFact]
    public void IntegerOverflow_Int32RemCallCompare_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt32RemCallCompareKernel, 1));

    [SkippableFact]
    public void IntegerOverflow_Int64RemCallCompare_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt64RemCallCompareKernel, 1L));

    [SkippableFact]
    public void IntegerOverflow_Int32DivShift_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt32DivShiftKernel, -1));

    [SkippableFact]
    public void IntegerOverflow_Int64DivShift_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(ConstCase(IntegerMinValueConstantKernels.LaunchInt64DivShiftKernel, -2147483648L));

    private static DiffBuffers ConstBuffers<T>(int rows, bool withDenominators)
        where T : unmanaged, INumberBase<T>
    {
        var buffers = new DiffBuffers();
        if (withDenominators)
        {
            var denominators = new T[rows];
            Array.Fill(denominators, -T.One);
            buffers.In("denoms", denominators, readOnly: true);
        }

        return buffers.Out<T>("dst", rows, OutputPolicy.Exact);
    }

    private static DifferentialCase ConstCase(
        Int32ScalarLaunch launch,
        int expected,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: static (_, shape) => ConstBuffers<int>(shape.Rows, withDenominators: false),
            shape: LaunchShapes.Linear(DefaultRows, 32),
            bufferName: "dst",
            reference: (b, _) => Array.Fill(b.Get<int>("dst"), expected),
            launchExpression: launchExpression);

    private static DifferentialCase ConstCase(
        Int64ScalarLaunch launch,
        long expected,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: static (_, shape) => ConstBuffers<long>(shape.Rows, withDenominators: false),
            shape: LaunchShapes.Linear(DefaultRows, 32),
            bufferName: "dst",
            reference: (b, _) => Array.Fill(b.Get<long>("dst"), expected),
            launchExpression: launchExpression);

    private static DifferentialCase ConstCase(
        Int32RowsLaunch launch,
        int expected,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: static (_, shape) => ConstBuffers<int>(shape.Rows, withDenominators: true),
            shape: LaunchShapes.Linear(DefaultRows, 32),
            inputName: "denoms",
            outputName: "dst",
            reference: (b, _) => Array.Fill(b.Get<int>("dst"), expected),
            launchExpression: launchExpression);

    private static DifferentialCase ConstCase(
        Int64RowsLaunch launch,
        long expected,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: static (_, shape) => ConstBuffers<long>(shape.Rows, withDenominators: true),
            shape: LaunchShapes.Linear(DefaultRows, 32),
            inputName: "denoms",
            outputName: "dst",
            reference: (b, _) => Array.Fill(b.Get<long>("dst"), expected),
            launchExpression: launchExpression);
}
