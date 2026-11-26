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
    internal static readonly DifferentialCase s_mathAbsInt32 = DiffCases.Create(
        IntegerTrapAndCheckedDifferentialKernels.LaunchMathAbsInt32Kernel,
        inputs: static (rng, shape) =>
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
        shape: LaunchShapes.Linear(DefaultRows, 32),
        inputName: "vals",
        seeds: [0, 1],
        oracleExceptions: OracleExceptionMode.MatchOracle);

    internal static readonly DifferentialCase s_mathAbsInt64 = DiffCases.Create(
        IntegerTrapAndCheckedDifferentialKernels.LaunchMathAbsInt64Kernel,
        inputs: static (rng, shape) =>
        {
            int rows = shape.Rows;
            long[] safeVals = [0, 1, -1, 2, -2, long.MaxValue, long.MinValue + 1, -4294967296L];
            var vals = new long[rows];
            for (int k = 0; k < rows; k++)
            {
                vals[k] = safeVals[k % safeVals.Length];
            }

            if (rng.Seed == 1)
            {
                vals[7] = long.MinValue;
            }

            return new DiffBuffers()
                .In("vals", vals, readOnly: true)
                .Out<long>("dst", rows, ["abs_int64"], OutputPolicy.Exact);
        },
        shape: LaunchShapes.Linear(DefaultRows, 32),
        inputName: "vals",
        seeds: [0, 1],
        oracleExceptions: OracleExceptionMode.MatchOracle);

    internal static readonly DifferentialCase s_mathAbsInt16 = DiffCases.CreateRows<short, short>(
        IntegerTrapAndCheckedDifferentialKernels.LaunchMathAbsInt16Kernel,
        inputs: static (rng, shape) =>
        {
            int rows = shape.Rows;
            short[] safeVals = [0, 1, -1, 2, -2, short.MaxValue, short.MinValue + 1, -128];
            var vals = new short[rows];
            for (int k = 0; k < rows; k++)
            {
                vals[k] = safeVals[k % safeVals.Length];
            }

            if (rng.Seed == 1)
            {
                vals[7] = short.MinValue;
            }

            return new DiffBuffers()
                .In("vals", vals, readOnly: true)
                .Out<short>("dst", rows, ["abs_int16"], OutputPolicy.Exact);
        },
        shape: LaunchShapes.Linear(DefaultRows, 32),
        inputName: "vals",
        seeds: [0, 1],
        oracleExceptions: OracleExceptionMode.MatchOracle);

    internal static readonly DifferentialCase s_mathAbsSByte = DiffCases.CreateRows<sbyte, sbyte>(
        IntegerTrapAndCheckedDifferentialKernels.LaunchMathAbsSByteKernel,
        inputs: static (rng, shape) =>
        {
            int rows = shape.Rows;
            sbyte[] safeVals = [0, 1, -1, 2, -2, sbyte.MaxValue, sbyte.MinValue + 1, -64];
            var vals = new sbyte[rows];
            for (int k = 0; k < rows; k++)
            {
                vals[k] = safeVals[k % safeVals.Length];
            }

            if (rng.Seed == 1)
            {
                vals[7] = sbyte.MinValue;
            }

            return new DiffBuffers()
                .In("vals", vals, readOnly: true)
                .Out<sbyte>("dst", rows, ["abs_sbyte"], OutputPolicy.Exact);
        },
        shape: LaunchShapes.Linear(DefaultRows, 32),
        inputName: "vals",
        seeds: [0, 1],
        oracleExceptions: OracleExceptionMode.MatchOracle);

    internal static readonly DifferentialCase s_mathAbsIntPtr = DiffCases.Create(
        IntegerTrapAndCheckedDifferentialKernels.LaunchMathAbsIntPtrKernel,
        inputs: static (rng, shape) =>
        {
            int rows = shape.Rows;
            long[] safeVals =
            [
                0, 1, -1, 2, -2, -128, -(1L << 31), 1L << 31, -4294967296L, long.MinValue + 1, long.MaxValue,
            ];
            var vals = new long[rows];
            for (int k = 0; k < rows; k++)
            {
                vals[k] = safeVals[k % safeVals.Length];
            }

            if (rng.Seed == 1)
            {
                vals[7] = long.MinValue;
            }

            return new DiffBuffers()
                .In("vals", vals, readOnly: true)
                .Out<long>("dst", rows, ["abs_nint"], OutputPolicy.Exact);
        },
        shape: LaunchShapes.Linear(DefaultRows, 32),
        inputName: "vals",
        seeds: [0, 1],
        oracleExceptions: OracleExceptionMode.MatchOracle);

    internal static readonly DifferentialCase s_mathAbsSingle = DiffCases.Create(
        IntegerTrapAndCheckedDifferentialKernels.LaunchMathAbsSingleKernel,
        inputs: static (rng, shape) =>
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
        shape: LaunchShapes.Linear(DefaultRows, 32),
        inputName: "vals");

    internal static readonly DifferentialCase s_mathAbsDouble = DiffCases.CreateRows<double, double>(
        IntegerTrapAndCheckedDifferentialKernels.LaunchMathAbsDoubleKernel,
        inputs: static (_, shape) =>
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
        shape: LaunchShapes.Linear(DefaultRows, 32),
        inputName: "vals");

    internal static readonly DifferentialCase s_floatDivisionByZero = new(
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
            operands: new FloatDivisionOperands
            {
                SingleValues = b.Get<float>("fVals"),
                DoubleValues = b.Get<double>("dVals"),
            },
            results: new FloatDivisionResults
            {
                SingleResults = b.Get<float>("fDst"),
                DoubleResults = b.Get<double>("dDst"),
            },
            rows: s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_floatToIntegerConversion = DiffCases.CreateRows<float, double, int>(
        IntegerTrapAndCheckedDifferentialKernels.LaunchFloatToIntegerConversionKernel,
        inputs: static (rng, shape) =>
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
        shape: LaunchShapes.PartialWarp(74, 37),
        firstInputName: "fVals",
        secondInputName: "dVals");

    [SkippableFact]
    public void MathAbs_Int32_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathAbsInt32);

    [SkippableFact]
    public void MathAbs_Int64_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathAbsInt64);

    [SkippableFact]
    public void MathAbs_Int16_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathAbsInt16);

    [SkippableFact]
    public void MathAbs_SByte_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathAbsSByte);

    // mutant: MathAbsTranspilerRule.GetHelper drop `or SpecialType.System_IntPtr` (nint is cut to 8 bits)
    // input: seed 0 -128, 2^31 and long.MaxValue; seed 1 long.MinValue at row 7 (OverflowException on every path)
    [SkippableFact]
    public void MathAbs_IntPtr_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathAbsIntPtr);

    [SkippableFact]
    public void MathAbs_Single_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathAbsSingle);

    [SkippableFact]
    public void MathAbs_Double_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathAbsDouble);

    [SkippableFact]
    public void FloatDivision_DivideByZero_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_floatDivisionByZero);

    [SkippableFact]
    public void FloatConversion_FloatToInteger_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_floatToIntegerConversion);
}
