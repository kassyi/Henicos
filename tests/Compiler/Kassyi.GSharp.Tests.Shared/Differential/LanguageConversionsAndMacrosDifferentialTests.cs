#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for user-defined conversion operators (#963), <c>[GpuOperatorMacro]</c> expansions
/// (#478, #641, #711), and <c>using static</c> / type alias directives (#503).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
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

    private static int MacroAdd(int left, int right, int bias) => (left * 3) + right + bias;
    private static int MacroSub(int left, int right) => (left * 2) - right + 9;

    private static readonly DifferentialCase s_macroArithmetic = new(
        $"{nameof(LanguageConversionsAndMacrosDifferentialKernels)}.{nameof(LanguageConversionsAndMacrosDifferentialKernels.OperatorMacroArithmeticKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, ["direct_add", "direct_private_sub", "nested_right_assoc", "nested_left_assoc"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => LanguageConversionsAndMacrosDifferentialKernels.LaunchOperatorMacroArithmeticKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            var ys = b.Get<int>("ys");
            var r = b.Get<int>("r");
            const int Bias = 5;
            for (int tid = 0; tid < xs.Length; tid++)
            {
                int x = xs[tid] & 0xFFF;
                int y = ys[tid] & 0xFFF;
                int z = (x ^ y) & 0x3FF;
                int o = tid * 4;
                r[o + 0] = MacroAdd(x, y, Bias);
                r[o + 1] = MacroSub(x, y);
                r[o + 2] = MacroAdd(x, MacroSub(y, z), Bias);
                r[o + 3] = MacroAdd(MacroSub(x, y), z, Bias);
            }
        },
    };

    private static readonly DifferentialCase s_macroEvaluationOrder = new(
        $"{nameof(LanguageConversionsAndMacrosDifferentialKernels)}.{nameof(LanguageConversionsAndMacrosDifferentialKernels.OperatorMacroEvaluationOrderKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, ["nested_eval_order_val", "nested_eval_order_trace"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => LanguageConversionsAndMacrosDifferentialKernels.LaunchOperatorMacroEvaluationOrderKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            var ys = b.Get<int>("ys");
            var r = b.Get<int>("r");
            const int Bias = 5;
            for (int tid = 0; tid < xs.Length; tid++)
            {
                int x = xs[tid] & 0xFFF;
                int y = ys[tid] & 0xFFF;
                int z = (x ^ y) & 0x3FF;
                int o = tid * 2;
                r[o + 0] = MacroAdd(x + 1, MacroSub(y + 2, z + 3), Bias);
                r[o + 1] = 123;
            }
        },
    };

    private static readonly DifferentialCase s_macroCastAndShift = new(
        $"{nameof(LanguageConversionsAndMacrosDifferentialKernels)}.{nameof(LanguageConversionsAndMacrosDifferentialKernels.OperatorMacroCastAndShiftKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, ["parenthesized_cast_macro", "shift_overload_long_wins"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => LanguageConversionsAndMacrosDifferentialKernels.LaunchOperatorMacroCastAndShiftKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            var ys = b.Get<int>("ys");
            var r = b.Get<int>("r");
            const int Bias = 5;
            for (int tid = 0; tid < xs.Length; tid++)
            {
                int x = xs[tid] & 0xFFF;
                int y = ys[tid] & 0xFFF;
                int o = tid * 2;
                r[o + 0] = MacroAdd(x, y, Bias);
                r[o + 1] = x >> 2;
            }
        },
    };

    private static readonly DifferentialCase s_macroInDeviceFunction = new(
        $"{nameof(LanguageConversionsAndMacrosDifferentialKernels)}.{nameof(LanguageConversionsAndMacrosDifferentialKernels.OperatorMacroInDeviceFunctionKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, ["helper_fn_macro_and_ctx_calls"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => LanguageConversionsAndMacrosDifferentialKernels.LaunchOperatorMacroInDeviceFunctionKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            var ys = b.Get<int>("ys");
            var r = b.Get<int>("r");
            const int Bias = 5;
            for (int tid = 0; tid < xs.Length; tid++)
            {
                int x = xs[tid] & 0xFFF;
                int y = ys[tid] & 0xFFF;
                int helperSum = MacroAdd(x, y, Bias);
                int helperDiff = MacroSub(helperSum, x);
                const int TotalAddCalls = 1;
                r[tid] = (helperDiff * 10) + TotalAddCalls;
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
    public void Test_UserDefinedImplicitConversions_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_implicitConversions);

    [SkippableFact]
    public void Test_OperatorMacroArithmetic_MatchesTheReferenceOnEveryPath() => DifferentialRunner.Run(s_macroArithmetic);

    [SkippableFact]
    public void Test_OperatorMacroEvaluationOrder_MatchesTheReferenceOnEveryPath() => DifferentialRunner.Run(s_macroEvaluationOrder);

    [SkippableFact]
    public void Test_OperatorMacroCastAndShift_MatchesTheReferenceOnEveryPath() => DifferentialRunner.Run(s_macroCastAndShift);

    [SkippableFact]
    public void Test_OperatorMacroInDeviceFunction_MatchesTheReferenceOnEveryPath() => DifferentialRunner.Run(s_macroInDeviceFunction);

    [SkippableFact]
    public void Test_OperatorMacroAcrossBarrierAndUsingStatic_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_macroAcrossBarrierAndUsingStatic);
}
