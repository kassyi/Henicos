#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests of the <c>nint</c> and <c>nuint</c> operators in device code: every kernel runs on the managed
/// path, the native CPU path and CUDA, against the C# result of the same kernel. A zero divisor raises the exception C#
/// raises; <c>MinValue / -1</c> of <c>nint</c> gives the GPU result (MinValue, 0 for the remainder) on every path.
/// </summary>
/// <remarks>
/// The zero divisor seeds raise a device error on CUDA, which has one error slot per process, so the class runs alone.
/// </remarks>
[Collection(TestCollections.DeviceExclusive)]
public class NativeIntegerOperatorDifferentialTests
{
    private const int Rows = 320;

    private static readonly long[] s_edges =
    [
        0, 1, -1, 2, -2, 7, -7, long.MinValue, long.MaxValue, long.MinValue + 1, long.MaxValue - 1,
        1L << 32, -(1L << 32), 1L << 31, 1L << 62, 0x0123_4567_89AB_CDEF
    ];

    // Shift counts at, below and above the 64-bit width, negative ones and the extremes of int
    private static readonly long[] s_counts =
    [
        0, 1, 2, 31, 32, 33, 63, 64, 65, 127, 128, 200, -1, -2, -33, -64, -65, int.MinValue, int.MaxValue
    ];

    private static readonly int[] s_rotateCounts = [0, 1, 63, 64, 65, -1];

    private static readonly IReadOnlyList<LaunchShape> s_shapes =
        [LaunchShapes.Linear(Rows, 33), LaunchShapes.Linear(Rows, 128)];

    // Row i takes the value at i / stride, so that the pairs of two lists (stride: the length of the first) all occur
    private static T[] Cycle<T>(T[] values, int rows, int stride)
    {
        var result = new T[rows];
        for (int i = 0; i < rows; i++)
        {
            result[i] = values[i / stride % values.Length];
        }

        return result;
    }

    // model: the result that G# gives on every path for a signed division, where C# throws for MinValue / -1 (the
    // spec: implementation-defined in an unchecked context): MinValue, and 0 for the remainder
    private static DifferentialCase Binary(string kernel, string kind,
        Action<long[], long[], long[], int, uint, uint> launch, Func<long, long, long>? model = null) =>
        new($"{nameof(NativeIntegerOperatorKernels)}.{kernel}Kernel")
        {
            Shapes = s_shapes,
            Seeds = kind switch { "div" => [0, 1, 2], "sdivzero" => [1], _ => [0] },
            OracleExceptions = OracleExceptionMode.MatchOracle,
            Inputs = (rng, shape) =>
            {
                var a = Cycle(s_edges, shape.Rows, 1);
                var b = Cycle(kind == "shift" ? s_counts : s_edges, shape.Rows, s_edges.Length);
                if (kind is "div" or "sdiv" or "sdivzero")
                {
                    // Seed 0 has no zero divisor. The unsigned kernels and the zero divisor kernels also avoid
                    // MinValue / -1, which C# throws for; the signed value kernels keep it (kernel parameters, so that
                    // no compiler folds it). Seed 1 has a zero divisor, seed 2 MinValue / -1 (unsigned kernels)
                    for (int i = 0; i < a.Length; i++)
                    {
                        if (b[i] == 0 || (kind != "sdiv" && b[i] == -1 && a[i] == long.MinValue))
                        {
                            b[i] = 1;
                        }
                    }

                    (a[5], b[5]) = kind == "sdiv" ? (a[5], b[5]) : rng.Seed switch
                    {
                        1 => (a[5], 0L),
                        2 => (long.MinValue, -1L),
                        _ => (a[5], b[5])
                    };
                }

                return new DiffBuffers()
                    .In("a", a, readOnly: true)
                    .In("b", b, readOnly: true)
                    .Out<long>("dst", shape.Rows, OutputPolicy.Exact);
            },
            Launch = (b, s) =>
                launch(b.Get<long>("a"), b.Get<long>("b"), b.Get<long>("dst"), s.Rows, s.GridX, s.BlockX),
            OracleIsLaunch = model is null,
            Reference = model is null
                ? null
                : (b, s) =>
                {
                    var a = b.Get<long>("a");
                    var divisors = b.Get<long>("b");
                    var dst = b.Get<long>("dst");
                    for (int i = 0; i < s.Rows; i++)
                    {
                        dst[i] = model(a[i], divisors[i]);
                    }
                },
        };

    private static DifferentialCase Unary(string kernel, Action<long[], long[], int, uint, uint> launch) =>
        new($"{nameof(NativeIntegerOperatorKernels)}.{kernel}Kernel")
        {
            Shapes = s_shapes,
            Inputs = static (rng, shape) => new DiffBuffers()
                .In("a", Cycle(s_edges, shape.Rows, 1), readOnly: true)
                .Out<long>("dst", shape.Rows, OutputPolicy.Exact),
            Launch = (b, s) => launch(b.Get<long>("a"), b.Get<long>("dst"), s.Rows, s.GridX, s.BlockX),
            OracleIsLaunch = true,
        };

    internal static readonly DifferentialCase s_nintAdd = Binary(
        "NintAdd", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintAddKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Add_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintAdd);

    internal static readonly DifferentialCase s_nintSubtract = Binary(
        "NintSubtract", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintSubtractKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Subtract_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintSubtract);

    internal static readonly DifferentialCase s_nintMultiply = Binary(
        "NintMultiply", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintMultiplyKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Multiply_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintMultiply);

    internal static readonly DifferentialCase s_nintAnd = Binary(
        "NintAnd", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintAndKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_And_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintAnd);

    internal static readonly DifferentialCase s_nintOr = Binary(
        "NintOr", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintOrKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Or_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintOr);

    internal static readonly DifferentialCase s_nintXor = Binary(
        "NintXor", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintXorKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Xor_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintXor);

    internal static readonly DifferentialCase s_nintLessThan = Binary(
        "NintLessThan", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintLessThanKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_LessThan_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintLessThan);

    internal static readonly DifferentialCase s_nintDivide = Binary(
        "NintDivide", "sdiv",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintDivideKernel(a, b, d, n, gridDimX: g, blockDimX: x),
        static (a, b) => b == -1 ? unchecked(-a) : a / b);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Divide_MatchesTheGpuResultOnEveryPath() =>
        DifferentialRunner.Run(s_nintDivide);

    internal static readonly DifferentialCase s_nintDivideByZero = Binary(
        "NintDivide", "sdivzero",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintDivideKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_DivideByZero_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintDivideByZero);

    internal static readonly DifferentialCase s_nintRemainder = Binary(
        "NintRemainder", "sdiv",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintRemainderKernel(a, b, d, n, gridDimX: g, blockDimX: x),
        static (a, b) => b == -1 ? 0 : a % b);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Remainder_MatchesTheGpuResultOnEveryPath() =>
        DifferentialRunner.Run(s_nintRemainder);

    internal static readonly DifferentialCase s_nintRemainderByZero = Binary(
        "NintRemainder", "sdivzero",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintRemainderKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_RemainderByZero_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintRemainderByZero);

    internal static readonly DifferentialCase s_nintShiftLeft = Binary(
        "NintShiftLeft", "shift",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintShiftLeftKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_ShiftLeft_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintShiftLeft);

    internal static readonly DifferentialCase s_nintShiftRight = Binary(
        "NintShiftRight", "shift",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintShiftRightKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_ShiftRight_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintShiftRight);

    internal static readonly DifferentialCase s_nintUnsignedShiftRight = Binary(
        "NintUnsignedShiftRight", "shift",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintUnsignedShiftRightKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_UnsignedShiftRight_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintUnsignedShiftRight);

    internal static readonly DifferentialCase s_nintNegate = Unary(
        "NintNegate",
        static (a, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintNegateKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Negate_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintNegate);

    internal static readonly DifferentialCase s_nintComplement = Unary(
        "NintComplement",
        static (a, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintComplementKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Complement_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintComplement);

    internal static readonly DifferentialCase s_nintIncrement = Unary(
        "NintIncrement",
        static (a, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintIncrementKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Increment_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintIncrement);

    internal static readonly DifferentialCase s_nintDecrement = Unary(
        "NintDecrement",
        static (a, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintDecrementKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Decrement_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintDecrement);

    internal static readonly DifferentialCase s_nintAddAssign = Binary(
        "NintAddAssign", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintAddAssignKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_AddAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintAddAssign);

    internal static readonly DifferentialCase s_nintSubtractAssign = Binary(
        "NintSubtractAssign", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintSubtractAssignKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_SubtractAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintSubtractAssign);

    internal static readonly DifferentialCase s_nintMultiplyAssign = Binary(
        "NintMultiplyAssign", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintMultiplyAssignKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_MultiplyAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintMultiplyAssign);

    internal static readonly DifferentialCase s_nintDivideAssign = Binary(
        "NintDivideAssign", "sdiv",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintDivideAssignKernel(a, b, d, n, gridDimX: g, blockDimX: x),
        static (a, b) => b == -1 ? unchecked(-a) : a / b);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_DivideAssign_MatchesTheGpuResultOnEveryPath() =>
        DifferentialRunner.Run(s_nintDivideAssign);

    internal static readonly DifferentialCase s_nintDivideAssignByZero = Binary(
        "NintDivideAssign", "sdivzero",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintDivideAssignKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_DivideAssignByZero_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintDivideAssignByZero);

    internal static readonly DifferentialCase s_nintRemainderAssign = Binary(
        "NintRemainderAssign", "sdiv",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintRemainderAssignKernel(a, b, d, n, gridDimX: g, blockDimX: x),
        static (a, b) => b == -1 ? 0 : a % b);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_RemainderAssign_MatchesTheGpuResultOnEveryPath() =>
        DifferentialRunner.Run(s_nintRemainderAssign);

    internal static readonly DifferentialCase s_nintRemainderAssignByZero = Binary(
        "NintRemainderAssign", "sdivzero",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintRemainderAssignKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_RemainderAssignByZero_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintRemainderAssignByZero);

    internal static readonly DifferentialCase s_nintShiftLeftAssign = Binary(
        "NintShiftLeftAssign", "shift",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintShiftLeftAssignKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_ShiftLeftAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintShiftLeftAssign);

    internal static readonly DifferentialCase s_nintShiftRightAssign = Binary(
        "NintShiftRightAssign", "shift",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintShiftRightAssignKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_ShiftRightAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintShiftRightAssign);

    internal static readonly DifferentialCase s_nintUnsignedShiftRightAssign = Binary(
        "NintUnsignedShiftRightAssign", "shift",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNintUnsignedShiftRightAssignKernel(
                a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_UnsignedShiftRightAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintUnsignedShiftRightAssign);

    internal static readonly DifferentialCase s_nuintAdd = Binary(
        "NuintAdd", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintAddKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_Add_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintAdd);

    internal static readonly DifferentialCase s_nuintSubtract = Binary(
        "NuintSubtract", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintSubtractKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_Subtract_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintSubtract);

    internal static readonly DifferentialCase s_nuintMultiply = Binary(
        "NuintMultiply", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintMultiplyKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_Multiply_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintMultiply);

    internal static readonly DifferentialCase s_nuintLessThan = Binary(
        "NuintLessThan", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintLessThanKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_LessThan_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintLessThan);

    internal static readonly DifferentialCase s_nuintDivide = Binary(
        "NuintDivide", "div",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintDivideKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_Divide_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintDivide);

    internal static readonly DifferentialCase s_nuintRemainder = Binary(
        "NuintRemainder", "div",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintRemainderKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_Remainder_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintRemainder);

    internal static readonly DifferentialCase s_nuintShiftLeft = Binary(
        "NuintShiftLeft", "shift",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintShiftLeftKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_ShiftLeft_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintShiftLeft);

    internal static readonly DifferentialCase s_nuintShiftRight = Binary(
        "NuintShiftRight", "shift",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintShiftRightKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_ShiftRight_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintShiftRight);

    internal static readonly DifferentialCase s_nuintUnsignedShiftRight = Binary(
        "NuintUnsignedShiftRight", "shift",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintUnsignedShiftRightKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_UnsignedShiftRight_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintUnsignedShiftRight);

    internal static readonly DifferentialCase s_nuintComplement = Unary(
        "NuintComplement",
        static (a, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintComplementKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_Complement_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintComplement);

    internal static readonly DifferentialCase s_nuintAddAssign = Binary(
        "NuintAddAssign", "pair",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintAddAssignKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_AddAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintAddAssign);

    internal static readonly DifferentialCase s_nuintDivideAssign = Binary(
        "NuintDivideAssign", "div",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintDivideAssignKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_DivideAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintDivideAssign);

    internal static readonly DifferentialCase s_nuintShiftLeftAssign = Binary(
        "NuintShiftLeftAssign", "shift",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintShiftLeftAssignKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_ShiftLeftAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintShiftLeftAssign);

    internal static readonly DifferentialCase s_nuintShiftRightAssign = Binary(
        "NuintShiftRightAssign", "shift",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintShiftRightAssignKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_ShiftRightAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintShiftRightAssign);

    internal static readonly DifferentialCase s_nuintUnsignedShiftRightAssign = Binary(
        "NuintUnsignedShiftRightAssign", "shift",
        static (a, b, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintUnsignedShiftRightAssignKernel(
                a, b, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_UnsignedShiftRightAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintUnsignedShiftRightAssign);

    internal static readonly DifferentialCase s_nuintLeadingZeroCount = Unary(
        "NuintLeadingZeroCount",
        static (a, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintLeadingZeroCountKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_BitOperationsLeadingZeroCount_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintLeadingZeroCount);

    internal static readonly DifferentialCase s_nuintTrailingZeroCount = Unary(
        "NuintTrailingZeroCount",
        static (a, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintTrailingZeroCountKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_BitOperationsTrailingZeroCount_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintTrailingZeroCount);

    internal static readonly DifferentialCase s_nuintPopCount = Unary(
        "NuintPopCount",
        static (a, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintPopCountKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_BitOperationsPopCount_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintPopCount);

    internal static readonly DifferentialCase s_nuintLog2 = Unary(
        "NuintLog2",
        static (a, d, n, g, x) =>
            NativeIntegerOperatorKernels.LaunchNuintLog2Kernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_BitOperationsLog2_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintLog2);

    internal static readonly DifferentialCase s_nuintRotateLeft = new(
        $"{nameof(NativeIntegerOperatorKernels)}.{nameof(NativeIntegerOperatorKernels.NuintRotateLeftKernel)}")
    {
        Shapes = s_shapes,
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("a", Cycle(s_edges, shape.Rows, 1), readOnly: true)
            .In("counts", Cycle(s_rotateCounts, shape.Rows, s_edges.Length), readOnly: true)
            .Out<long>("dst", shape.Rows, OutputPolicy.Exact),
        Launch = static (b, s) => NativeIntegerOperatorKernels.LaunchNuintRotateLeftKernel(
            b.Get<long>("a"), b.Get<int>("counts"), b.Get<long>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_BitOperationsRotateLeft_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintRotateLeft);

    internal static readonly DifferentialCase s_nuintRotateRight = new(
        $"{nameof(NativeIntegerOperatorKernels)}.{nameof(NativeIntegerOperatorKernels.NuintRotateRightKernel)}")
    {
        Shapes = s_shapes,
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("a", Cycle(s_edges, shape.Rows, 1), readOnly: true)
            .In("counts", Cycle(s_rotateCounts, shape.Rows, s_edges.Length), readOnly: true)
            .Out<long>("dst", shape.Rows, OutputPolicy.Exact),
        Launch = static (b, s) => NativeIntegerOperatorKernels.LaunchNuintRotateRightKernel(
            b.Get<long>("a"), b.Get<int>("counts"), b.Get<long>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_BitOperationsRotateRight_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintRotateRight);
}
