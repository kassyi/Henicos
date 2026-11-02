#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for user-defined conversion operators (#963), <c>[GpuOperatorMacro]</c> expansions
/// (#478, #641, #711), and <c>using static</c> / type alias directives (#503) (Lane D6 Stage 2).
/// </summary>
public class LanguageConversionsAndMacrosDifferentialTests
{
    private const int Rows = 128;

    private static readonly DifferentialCase s_implicitConversions = new(
        $"{nameof(LanguageConversionsAndMacrosDifferentialKernels)}.{nameof(LanguageConversionsAndMacrosDifferentialKernels.UserDefinedImplicitConversionsKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, LanguageConversionsAndMacrosDifferentialKernels.ImplicitConversionsColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => LanguageConversionsAndMacrosDifferentialKernels.LaunchUserDefinedImplicitConversionsKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => LanguageConversionsAndMacrosDifferentialKernels.UserDefinedImplicitConversionsKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_operatorMacros = new(
        $"{nameof(LanguageConversionsAndMacrosDifferentialKernels)}.{nameof(LanguageConversionsAndMacrosDifferentialKernels.OperatorMacrosAndEvaluationOrderKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, LanguageConversionsAndMacrosDifferentialKernels.OperatorMacrosColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => LanguageConversionsAndMacrosDifferentialKernels.LaunchOperatorMacrosAndEvaluationOrderKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, _) =>
        {
            static int MacroAdd(int left, int right, int bias) => (left * 3) + right + bias;
            static int MacroSub(int left, int right) => (left * 2) - right + 9;

            var xs = b.Get<int>("xs");
            var ys = b.Get<int>("ys");
            var r = b.Get<int>("r");
            const int Bias = 5;

            for (int tid = 0; tid < xs.Length; tid++)
            {
                int x = xs[tid] & 0xFFF;
                int y = ys[tid] & 0xFFF;
                int z = (x ^ y) & 0x3FF;
                int o = tid * 9;

                int cAdd = MacroAdd(x, y, Bias);
                int cSub = MacroSub(x, y);
                int cRight = MacroAdd(x, MacroSub(y, z), Bias);
                int cLeft = MacroAdd(MacroSub(x, y), z, Bias);
                int cOrder = MacroAdd(x + 1, MacroSub(y + 2, z + 3), Bias);
                int cCast = MacroAdd(x, y, Bias);
                int cShift = x >> 2;

                int helperSum = MacroAdd(x, y, Bias);
                int helperDiff = MacroSub(helperSum, x);
                // Calls to AddWithContext: cAdd (1), cRight (1), cLeft (1), cOrder (1), cCast (1), helperSum (1) = 6
                const int TotalAddCalls = 6;

                r[o + 0] = cAdd;
                r[o + 1] = cSub;
                r[o + 2] = cRight;
                r[o + 3] = cLeft;
                r[o + 4] = cOrder;
                r[o + 5] = 123;
                r[o + 6] = cCast;
                r[o + 7] = cShift;
                r[o + 8] = (helperDiff * 10) + TotalAddCalls;
            }
        },
    };

    private static readonly DifferentialCase s_macroAcrossBarrierAndUsingStatic = new(
        $"{nameof(LanguageConversionsAndMacrosDifferentialKernels)}.{nameof(LanguageConversionsAndMacrosDifferentialKernels.OperatorMacroAcrossBarrierAndUsingStaticKernel)}")
    {
        Shape = LaunchShapes.Grid1D(gridX: 1, blockX: 64, rows: 64),
        Inputs = static (rng, shape) =>
        {
            var xs = DiffValues.Int32(rng, shape.Rows);
            return new DiffBuffers()
                .In("xs", xs)
                .Out<int>("scratch", shape.Rows, OutputPolicy.Exact)
                .Out<int>("r", shape.Rows, LanguageConversionsAndMacrosDifferentialKernels.MacroAcrossBarrierAndUsingStaticColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => LanguageConversionsAndMacrosDifferentialKernels.LaunchOperatorMacroAcrossBarrierAndUsingStaticKernel(
            b.Get<int>("xs"),
            b.Get<int>("scratch"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, s) =>
        {
            var xs = b.Get<int>("xs");
            var scratch = b.Get<int>("scratch");
            var r = b.Get<int>("r");
            int blockDim = (int)s.BlockX;
            var preVals = new int[blockDim];

            for (int t = 0; t < blockDim; t++)
            {
                int x = xs[t] & 0xFFF;
                int y = (x * 3) & 0x7FF;
                preVals[t] = (x * 3) + y + 7;
            }

            for (int t = 0; t < blockDim; t++)
            {
                int x = xs[t] & 0xFFF;
                scratch[t] = x + t;
            }

            for (int t = 0; t < blockDim; t++)
            {
                int x = xs[t] & 0xFFF;
                int y = (x * 3) & 0x7FF;
                int preNeighbor = preVals[(t + 1) % blockDim];
                int suspendedVal = (preVals[t] * 2) - preNeighbor + 9;
                int neighbor = scratch[(t + 3) % blockDim];
                int laneZeroX = xs[t & ~31] & 0xFFF;
                int lzc = BitOperations.LeadingZeroCount((uint)x | 1u);
                int clamped = Math.Clamp(Math.Abs(x - y), 0, 500);

                int o = t * 5;
                r[o + 0] = suspendedVal;
                r[o + 1] = 1;
                r[o + 2] = neighbor;
                r[o + 3] = (lzc * 1000) + clamped + laneZeroX;
                r[o + 4] = 343;
            }
        },
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_UserDefinedImplicitConversions_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_implicitConversions);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_OperatorMacrosAndEvaluationOrder_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_operatorMacros);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_OperatorMacroAcrossBarrierAndUsingStatic_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_macroAcrossBarrierAndUsingStatic);
}
