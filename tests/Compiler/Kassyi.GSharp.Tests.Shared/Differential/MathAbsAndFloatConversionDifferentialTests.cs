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
/// and <see cref="Math.Abs(double)"/> (#947), IEEE 754 float division by zero, and float-to-integer conversion
/// (#724, #890).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.IntegerTrapAndCheckedDifferential)]
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
    public void Test_FloatDivisionByZero_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_floatDivisionByZero);

    [SkippableFact]
    public void Test_FloatToIntegerConversion_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_floatToIntegerConversion);
}
