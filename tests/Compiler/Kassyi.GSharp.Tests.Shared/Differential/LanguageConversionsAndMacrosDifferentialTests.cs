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

    internal static readonly DifferentialCase s_implicitConversions = DiffCases.Create(
        LanguageConversionsAndMacrosDifferentialKernels.LaunchUserDefinedImplicitConversionsKernel,
        inputs: static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, LanguageConversionsAndMacrosDifferentialKernels.ImplicitConversionsColumns, OutputPolicy.Exact);
        },
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        firstInputName: "xs",
        secondInputName: "ys",
        outputName: "r");

    private static int MacroAdd(int left, int right, int bias) => (left * 3) + right + bias;
    private static int MacroSub(int left, int right) => (left * 2) - right + 9;

    internal static readonly DifferentialCase s_macroArithmetic = DiffCases.Create(
        LanguageConversionsAndMacrosDifferentialKernels.LaunchOperatorMacroArithmeticKernel,
        inputs: static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, ["direct_add", "direct_private_sub", "nested_right_assoc", "nested_left_assoc"], OutputPolicy.Exact);
        },
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        firstInputName: "xs",
        secondInputName: "ys",
        outputName: "r",
        reference: static (b, _) =>
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
        });

    internal static readonly DifferentialCase s_macroEvaluationOrder = DiffCases.Create(
        LanguageConversionsAndMacrosDifferentialKernels.LaunchOperatorMacroEvaluationOrderKernel,
        inputs: static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, ["nested_eval_order_val", "nested_eval_order_trace"], OutputPolicy.Exact);
        },
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        firstInputName: "xs",
        secondInputName: "ys",
        outputName: "r",
        reference: static (b, _) =>
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
        });

    internal static readonly DifferentialCase s_macroCastAndShift = DiffCases.Create(
        LanguageConversionsAndMacrosDifferentialKernels.LaunchOperatorMacroCastAndShiftKernel,
        inputs: static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, ["parenthesized_cast_macro", "shift_overload_long_wins"], OutputPolicy.Exact);
        },
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        firstInputName: "xs",
        secondInputName: "ys",
        outputName: "r",
        reference: static (b, _) =>
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
        });

    internal static readonly DifferentialCase s_macroInDeviceFunction = DiffCases.Create(
        LanguageConversionsAndMacrosDifferentialKernels.LaunchOperatorMacroInDeviceFunctionKernel,
        inputs: static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, ["helper_fn_macro_and_ctx_calls"], OutputPolicy.Exact);
        },
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        firstInputName: "xs",
        secondInputName: "ys",
        outputName: "r",
        reference: static (b, _) =>
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
        });

    internal static readonly DifferentialCase s_macroAcrossBarrierAndUsingStatic = DiffCases.Create(
        LanguageConversionsAndMacrosDifferentialKernels.LaunchOperatorMacroAcrossBarrierAndUsingStaticKernel,
        inputs: static (rng, shape) =>
        {
            var xs = DiffValues.Int32(rng, shape.Rows);
            return new DiffBuffers()
                .In("xs", xs)
                .Out<int>("scratch", shape.Rows, OutputPolicy.Exact)
                .Out<int>("r", shape.Rows, LanguageConversionsAndMacrosDifferentialKernels.MacroAcrossBarrierAndUsingStaticColumns, OutputPolicy.Exact);
        },
        shape: LaunchShapes.Grid1D(gridX: 1, blockX: 64, rows: 64),
        firstInputName: "xs",
        secondInputName: "scratch",
        outputName: "r",
        reference: static (b, s) =>
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
        });

    [SkippableFact]
    public void Conversion_UserDefinedImplicit_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_implicitConversions);

    [SkippableFact]
    public void OperatorMacro_Arithmetic_MatchesTheReferenceOnEveryPath() => DifferentialRunner.Run(s_macroArithmetic);

    [SkippableFact]
    public void OperatorMacro_EvaluationOrder_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_macroEvaluationOrder);

    [SkippableFact]
    public void OperatorMacro_CastAndShift_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_macroCastAndShift);

    [SkippableFact]
    public void OperatorMacro_InDeviceFunction_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_macroInDeviceFunction);

    [SkippableFact]
    public void OperatorMacro_AcrossBarrierAndUsingStatic_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_macroAcrossBarrierAndUsingStatic);
}
