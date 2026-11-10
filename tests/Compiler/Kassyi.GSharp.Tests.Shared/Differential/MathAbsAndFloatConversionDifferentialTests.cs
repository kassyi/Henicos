#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.IntegerTrapAndCheckedDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <see cref="Math.Abs(int)"/>, <see cref="Math.Abs(float)"/>,
/// and <see cref="Math.Abs(double)"/> (#947), shift-count masking boundary matrix across
/// negative and &gt;= width counts (#722), <c>unchecked</c> arithmetic wrap-around,
/// IEEE 754 float division by zero, and float-to-integer conversion (#724, #890).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.GlobalStateSequential)]
public class MathAbsAndFloatConversionDifferentialTests
{
    private static readonly DifferentialCase s_mathAbsInt32 = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.MathAbsInt32Kernel)))
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
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.MathAbsSingleKernel)))
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
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.MathAbsDoubleKernel)))
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
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.ShiftCountMaskBoundaryMatrixKernel)))
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        ExtraShapesSeed0Only =
        [
            LaunchShapes.Linear(64, 32),
        ],
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            int[] shiftCounts =
            [
                -1, -31, -32, -63, -64, 0, 1, 7, 15, 16, 31, 32, 33, 63, 64, 65,
                int.MinValue, int.MaxValue,
            ];
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
                .Out<int>(
                    "dst",
                    rows,
                    IntegerTrapAndCheckedDifferentialKernels.ShiftMaskBoundaryColumns,
                    OutputPolicy.Exact);
        },
        Launch = static (b, s) =>
            IntegerTrapAndCheckedDifferentialKernels.LaunchShiftCountMaskBoundaryMatrixKernel(
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
        Oracle = static (b, s) =>
            IntegerTrapAndCheckedDifferentialKernels.ShiftCountMaskBoundaryMatrixKernel(
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
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.UncheckedIntegerWrapKernel)))
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var (left, right) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("left", left, readOnly: true)
                .In("right", right, readOnly: true)
                .Out<int>(
                    "dst",
                    rows,
                    IntegerTrapAndCheckedDifferentialKernels.UncheckedIntegerWrapColumns,
                    OutputPolicy.Exact);
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
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.FloatDivisionByZeroKernel)))
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
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.FloatToIntegerConversionKernel)))
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
                .Out<int>(
                    "dst",
                    rows,
                    IntegerTrapAndCheckedDifferentialKernels.FloatToIntegerConversionColumns,
                    OutputPolicy.Exact);
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
