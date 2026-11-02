#nullable enable
using System;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for Lane D5 Stage 3: <see cref="Nullable{T}.Value"/> (#718) and explicit <c>(T)nullable</c> cast
/// (S28), <c>Math.Clamp</c> (#718), <c>StackLinq</c> empty and multi-element sequence traps, <c>ElementAtOrDefault(-1)</c>
/// non-Span enumeration side effects (S31), <c>MemoryExtensions.Sort</c> length mismatch and comparer exception wrapping,
/// and post-trap non-continuation before <c>100 / denom</c> (S30 / #729).
/// </summary>
public class BclExceptionDifferentialTests
{
    private const int DefaultRows = 64;
    private const int SliceWidth = BclExceptionDifferentialKernels.SliceWidth;

    private static readonly (double Val, double Min, double Max)[] s_validClampRows =
    [
        (0.5, 0.0, 1.0),
        (2.0, 0.0, 1.0),
        (-1.0, 0.0, 1.0),
        (double.NaN, 0.0, 1.0),
        (-0.0, 0.0, 1.0),
        (0.0, -0.0, 1.0),
        (0.5, double.NaN, 1.0),
        (0.5, 0.0, double.NaN),
        (1.0, 0.0, 1.0),
        (0.0, 0.0, 1.0),
    ];

    // L-01: Nullable<T>.Value on int? and user-defined struct? (#718).
    private static readonly DifferentialCase s_nullableValueAccess = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.NullableValueAccessKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed | DiffPath.Native,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var hasInt = new int[rows];
            var intVals = DiffValues.Int32(rng, rows);
            var hasPair = new int[rows];
            var pairX = DiffValues.Int32(rng, rows);
            var pairY = DiffValues.Int32(rng, rows);
            Array.Fill(hasInt, 1);
            Array.Fill(hasPair, 1);

            if (rng.Seed == 1)
            {
                hasInt[5] = 0;
            }
            else if (rng.Seed == 2)
            {
                hasPair[13] = 0;
            }

            return new DiffBuffers()
                .In("hasInt", hasInt, readOnly: true)
                .In("intVals", intVals, readOnly: true)
                .In("hasPair", hasPair, readOnly: true)
                .In("pairX", pairX, readOnly: true)
                .In("pairY", pairY, readOnly: true)
                .Out<int>("dst", rows, BclExceptionDifferentialKernels.NullableValueColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchNullableValueAccessKernel(
            b.Get<int>("hasInt"),
            b.Get<int>("intVals"),
            b.Get<int>("hasPair"),
            b.Get<int>("pairX"),
            b.Get<int>("pairY"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.NullableValueAccessKernel(
            b.Get<int>("hasInt"),
            b.Get<int>("intVals"),
            b.Get<int>("hasPair"),
            b.Get<int>("pairX"),
            b.Get<int>("pairY"),
            b.Get<int>("dst"),
            s.Rows),
    };

    // L-01 (S28): Explicit (T)nullable cast and GetValueOrDefault on CPU 2-path (transpiler emits invalid C++ ((int)(opt)) without .Value, and System_Nullable<T> lacks GetValueOrDefault).
    private static void NullableExplicitCastStep(
        int t,
        int[] hasInt,
        int[] intVals,
        int[] hasPair,
        int[] pairX,
        int[] pairY,
        int[] dst)
    {
        int? optInt = hasInt[t] != 0 ? intVals[t] : null;
        DiffNullablePairStruct? optPair = hasPair[t] != 0
            ? new DiffNullablePairStruct { X = pairX[t], Y = pairY[t] }
            : null;

        int defInt = optInt.GetValueOrDefault(-999) + optInt.GetValueOrDefault();
        DiffNullablePairStruct defPair = optPair.GetValueOrDefault(new DiffNullablePairStruct { X = -10, Y = -20 });
#pragma warning disable CS8629 // Deliberate explicit cast on Nullable<T> to verify InvalidOperationException when null
        int castInt = (int)optInt;
        DiffNullablePairStruct castPair = (DiffNullablePairStruct)optPair;
#pragma warning restore CS8629
        dst[t] = (castInt + defInt) ^ (castPair.X + castPair.Y + defPair.X + defPair.Y);
    }

    private static readonly DifferentialCase s_nullableExplicitCastCpuManaged = new(
        "BclExceptionDifferentialTests.NullableExplicitCastCpuManaged")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var hasInt = new int[rows];
            var intVals = DiffValues.Int32(rng, rows);
            var hasPair = new int[rows];
            var pairX = DiffValues.Int32(rng, rows);
            var pairY = DiffValues.Int32(rng, rows);
            Array.Fill(hasInt, 1);
            Array.Fill(hasPair, 1);

            if (rng.Seed == 1)
            {
                hasInt[3] = 0;
            }
            else if (rng.Seed == 2)
            {
                hasPair[11] = 0;
            }

            return new DiffBuffers()
                .In("hasInt", hasInt, readOnly: true)
                .In("intVals", intVals, readOnly: true)
                .In("hasPair", hasPair, readOnly: true)
                .In("pairX", pairX, readOnly: true)
                .In("pairY", pairY, readOnly: true)
                .Out<int>("dst", rows, ["explicit_cast_nullable"], OutputPolicy.Exact);
        },
        Launch = static (b, s) =>
        {
            var hasInt = b.Get<int>("hasInt");
            var intVals = b.Get<int>("intVals");
            var hasPair = b.Get<int>("hasPair");
            var pairX = b.Get<int>("pairX");
            var pairY = b.Get<int>("pairY");
            var dst = b.Get<int>("dst");
            for (int t = 0; t < s.Rows; t++)
            {
                NullableExplicitCastStep(t, hasInt, intVals, hasPair, pairX, pairY, dst);
            }
        },
        Oracle = static (b, s) =>
        {
            int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
            if (t >= s.Rows)
            {
                return;
            }

            NullableExplicitCastStep(
                t,
                b.Get<int>("hasInt"),
                b.Get<int>("intVals"),
                b.Get<int>("hasPair"),
                b.Get<int>("pairX"),
                b.Get<int>("pairY"),
                b.Get<int>("dst"));
        },
    };

    // L-02: Math.Clamp(val, min, max) across int, float, double (#718).
    private static readonly DifferentialCase s_mathClampBoundsAndNaN = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.MathClampBoundsAndNaNKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed | DiffPath.Native,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var iVal = DiffValues.Int32(rng, rows);
            var iMin = new int[rows];
            var iMax = new int[rows];
            var fVal = new float[rows];
            var fMin = new float[rows];
            var fMax = new float[rows];
            var dVal = new double[rows];
            var dMin = new double[rows];
            var dMax = new double[rows];

            for (int k = 0; k < rows; k++)
            {
                iMin[k] = -100 - k;
                iMax[k] = (k % 3 == 0) ? iMin[k] : (100 + k);

                var row = s_validClampRows[k % s_validClampRows.Length];
                fVal[k] = (float)row.Val;
                fMin[k] = (float)row.Min;
                fMax[k] = (float)row.Max;
                dVal[k] = row.Val;
                dMin[k] = row.Min;
                dMax[k] = row.Max;
            }

            switch (rng.Seed)
            {
                case 1:
                    iMin[3] = 10;
                    iMax[3] = -10;
                    break;
                case 2:
                    fMin[9] = 5.0f;
                    fMax[9] = 1.0f;
                    break;
                case 3:
                    dMin[17] = 100.0;
                    dMax[17] = -100.0;
                    break;
            }

            return new DiffBuffers()
                .In("iVal", iVal, readOnly: true)
                .In("iMin", iMin, readOnly: true)
                .In("iMax", iMax, readOnly: true)
                .In("fVal", fVal, readOnly: true)
                .In("fMin", fMin, readOnly: true)
                .In("fMax", fMax, readOnly: true)
                .In("dVal", dVal, readOnly: true)
                .In("dMin", dMin, readOnly: true)
                .In("dMax", dMax, readOnly: true)
                .Out<int>("iDst", rows, BclExceptionDifferentialKernels.MathClampIntColumns, OutputPolicy.Exact)
                .Out<float>("fDst", rows, BclExceptionDifferentialKernels.MathClampFloatColumns, OutputPolicy.Ulp(0))
                .Out<double>("dDst", rows, BclExceptionDifferentialKernels.MathClampDoubleColumns, OutputPolicy.Ulp(0));
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchMathClampBoundsAndNaNKernel(
            b.Get<int>("iVal"),
            b.Get<int>("iMin"),
            b.Get<int>("iMax"),
            b.Get<float>("fVal"),
            b.Get<float>("fMin"),
            b.Get<float>("fMax"),
            b.Get<double>("dVal"),
            b.Get<double>("dMin"),
            b.Get<double>("dMax"),
            b.Get<int>("iDst"),
            b.Get<float>("fDst"),
            b.Get<double>("dDst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.MathClampBoundsAndNaNKernel(
            b.Get<int>("iVal"),
            b.Get<int>("iMin"),
            b.Get<int>("iMax"),
            b.Get<float>("fVal"),
            b.Get<float>("fMin"),
            b.Get<float>("fMax"),
            b.Get<double>("dVal"),
            b.Get<double>("dMin"),
            b.Get<double>("dMax"),
            b.Get<int>("iDst"),
            b.Get<float>("fDst"),
            b.Get<double>("dDst"),
            s.Rows),
    };

    // L-03: StackLinq First, Last, Single, SingleOrDefault, Min, Max, Average, Aggregate traps on both Managed and Native.
    private static readonly DifferentialCase s_stackLinqEmptyAndMultiElementTrap = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.StackLinqEmptyAndMultiElementTrapKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed | DiffPath.Native,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = new int[rows * SliceWidth];
            var lengths = new int[rows];
            var modes = new int[rows];

            for (int k = 0; k < rows; k++)
            {
                lengths[k] = SliceWidth;
                modes[k] = 0;
                for (int j = 0; j < SliceWidth; j++)
                {
                    backing[(k * SliceWidth) + j] = ((k + 1) * 10) + j + 1;
                }
            }

            switch (rng.Seed)
            {
                case 1:
                    // First() on empty sequence.
                    modes[1] = 1;
                    lengths[1] = 0;
                    break;
                case 2:
                    // First(pred) with no matching element.
                    modes[2] = 2;
                    lengths[2] = SliceWidth;
                    break;
                case 3:
                    // Last() on empty sequence.
                    modes[3] = 3;
                    lengths[3] = 0;
                    break;
                case 4:
                    // Single() on empty sequence.
                    modes[4] = 4;
                    lengths[4] = 0;
                    break;
                case 5:
                    // Single() on 2-element sequence.
                    modes[5] = 4;
                    lengths[5] = 2;
                    break;
                case 6:
                    // Single(pred) with 2 matching elements.
                    modes[6] = 5;
                    lengths[6] = 2;
                    break;
                case 7:
                    // SingleOrDefault() on 2-element sequence.
                    modes[7] = 6;
                    lengths[7] = 2;
                    break;
                case 8:
                    // Min() on empty sequence.
                    modes[8] = 7;
                    lengths[8] = 0;
                    break;
                case 9:
                    // Max() on empty sequence.
                    modes[9] = 8;
                    lengths[9] = 0;
                    break;
                case 10:
                    // Average() / Aggregate() on empty sequence.
                    modes[10] = 9;
                    lengths[10] = 0;
                    modes[11] = 10;
                    lengths[11] = 0;
                    break;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("lengths", lengths, readOnly: true)
                .In("modes", modes, readOnly: true)
                .Out<int>("dst", rows, BclExceptionDifferentialKernels.StackLinqTrapColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchStackLinqEmptyAndMultiElementTrapKernel(
            b.Get<int>("backing"),
            b.Get<int>("lengths"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.StackLinqEmptyAndMultiElementTrapKernel(
            b.Get<int>("backing"),
            b.Get<int>("lengths"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            s.Rows),
    };

    // L-04: StackLinq ElementAt(index) ArgumentOutOfRangeException on both Managed and Native, and ElementAtOrDefault(-1).
    private static readonly DifferentialCase s_stackLinqElementAtAndNegativeDefault = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.StackLinqElementAtAndNegativeDefaultKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed | DiffPath.Native,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = new int[rows * SliceWidth];
            var indices = new int[rows];
            var modes = new int[rows];

            for (int k = 0; k < rows; k++)
            {
                indices[k] = k & (SliceWidth - 1);
                modes[k] = 0;
                for (int j = 0; j < SliceWidth; j++)
                {
                    backing[(k * SliceWidth) + j] = ((k + 1) * 10) + j + 1;
                }
            }

            if (rng.Seed == 1)
            {
                modes[4] = 1;
                indices[4] = -1;
            }
            else if (rng.Seed == 2)
            {
                modes[11] = 2;
                indices[11] = SliceWidth;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("indices", indices, readOnly: true)
                .In("modes", modes, readOnly: true)
                .Out<int>("dst", rows, BclExceptionDifferentialKernels.StackLinqElementAtColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchStackLinqElementAtAndNegativeDefaultKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.StackLinqElementAtAndNegativeDefaultKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            s.Rows),
    };

    // S31 witness: ElementAtOrDefault(-1) on non-Span Select sequence should return default immediately (0 evaluations)
    // per .NET Enumerable.ElementAtOrDefault specification, but currently evaluates all 4 upstream elements.
    private static readonly DifferentialCase s_elementAtOrDefaultNegativeIndexSideEffect = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.ElementAtOrDefaultNegativeIndexSideEffectReproKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0],
        Inputs = static (_, shape) =>
        {
            int rows = shape.Rows;
            var backing = new int[rows * SliceWidth];
            for (int i = 0; i < backing.Length; i++)
            {
                backing[i] = i + 1;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .Out<int>("dst", rows, ["neg_default_val", "select_neg_eval_count", "where_neg_eval_count"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchElementAtOrDefaultNegativeIndexSideEffectReproKernel(
            b.Get<int>("backing"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, s) =>
        {
            var dst = b.Get<int>("dst");
            for (int t = 0; t < s.Rows; t++)
            {
                dst[(t * 3) + 0] = 0;
                dst[(t * 3) + 1] = 0;
                dst[(t * 3) + 2] = 0;
            }
        },
    };

    // L-05: MemoryExtensions.Sort(keys, items) length mismatch (ArgumentException) and comparer trap (InvalidOperationException)
    // on both Managed and Native.
    private static readonly DifferentialCase s_memoryExtensionsSortException = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.MemoryExtensionsSortExceptionKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed | DiffPath.Native,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var keysBacking = new int[rows * SliceWidth];
            var itemsBacking = new int[rows * SliceWidth];
            var keyLens = new int[rows];
            var itemLens = new int[rows];
            var modes = new int[rows];

            for (int k = 0; k < rows; k++)
            {
                keyLens[k] = SliceWidth;
                itemLens[k] = SliceWidth;
                modes[k] = 0;
                for (int j = 0; j < SliceWidth; j++)
                {
                    int idx = (k * SliceWidth) + j;
                    keysBacking[idx] = (SliceWidth - j) + (k * 10);
                    itemsBacking[idx] = (j + 1) * 100;
                }
            }

            if (rng.Seed == 1)
            {
                modes[6] = 1;
                keyLens[6] = SliceWidth;
                itemLens[6] = SliceWidth - 2;
            }
            else if (rng.Seed == 2)
            {
                modes[14] = 2;
            }

            return new DiffBuffers()
                .In("keysBacking", keysBacking)
                .In("itemsBacking", itemsBacking)
                .In("keyLens", keyLens, readOnly: true)
                .In("itemLens", itemLens, readOnly: true)
                .In("modes", modes, readOnly: true)
                .Out<int>("dst", rows, BclExceptionDifferentialKernels.SortExceptionColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchMemoryExtensionsSortExceptionKernel(
            b.Get<int>("keysBacking"),
            b.Get<int>("itemsBacking"),
            b.Get<int>("keyLens"),
            b.Get<int>("itemLens"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.MemoryExtensionsSortExceptionKernel(
            b.Get<int>("keysBacking"),
            b.Get<int>("itemsBacking"),
            b.Get<int>("keyLens"),
            b.Get<int>("itemLens"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            s.Rows),
    };

    // L-06: Post-trap non-continuation before 100 / denom (S30 / #729).
    private static readonly DifferentialCase s_trapNonContinuationGuard = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.TrapNonContinuationGuardKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = new int[rows * SliceWidth];
            var thresholds = new int[rows];

            for (int k = 0; k < rows; k++)
            {
                thresholds[k] = 0;
                for (int j = 0; j < SliceWidth; j++)
                {
                    backing[(k * SliceWidth) + j] = j + 2;
                }
            }

            if (rng.Seed == 1)
            {
                thresholds[7] = 1000000;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("thresholds", thresholds, readOnly: true)
                .Out<int>("dst", rows, ["quot_after_first"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchTrapNonContinuationGuardKernel(
            b.Get<int>("backing"),
            b.Get<int>("thresholds"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.TrapNonContinuationGuardKernel(
            b.Get<int>("backing"),
            b.Get<int>("thresholds"),
            b.Get<int>("dst"),
            s.Rows),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullableValueAccess_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableValueAccess);

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullableExplicitCast_MatchesTheOracleOnCpuManaged()
    {
        var report = DifferentialRunner.Evaluate(
            s_nullableExplicitCastCpuManaged,
            s_nullableExplicitCastCpuManaged.Seeds!,
            [new DiffPathRunner(DiffPath.Managed, DiffPaths.DirectRunner)],
            KnownDivergences.All);
        Assert.False(report.HasFailures, report.ToString());
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MathClampBoundsAndNaN_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathClampBoundsAndNaN);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackLinqEmptyAndMultiElementTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_stackLinqEmptyAndMultiElementTrap);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackLinqElementAtAndNegativeDefault_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_stackLinqElementAtAndNegativeDefault);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ElementAtOrDefaultNegativeIndexSideEffect_MatchesReference() =>
        DifferentialRunner.Run(s_elementAtOrDefaultNegativeIndexSideEffect);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MemoryExtensionsSortException_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_memoryExtensionsSortException);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_TrapNonContinuationGuard_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_trapNonContinuationGuard);
}
