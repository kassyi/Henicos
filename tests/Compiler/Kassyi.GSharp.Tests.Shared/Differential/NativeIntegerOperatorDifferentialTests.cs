#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    private static DifferentialCase Binary(
        Int64PairRowsLaunch launch,
        string kind,
        Func<long, long, long>? model = null,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: (rng, shape) =>
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
            shapes: s_shapes,
            seeds: kind switch { "div" => [0, 1, 2], "sdivzero" => [1], _ => [0] },
            oracleExceptions: OracleExceptionMode.MatchOracle,
            reference: model is null
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
            launchExpression: launchExpression);

    private static DifferentialCase Unary(
        Int64RowsLaunch launch,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: static (_, shape) => new DiffBuffers()
                .In("a", Cycle(s_edges, shape.Rows, 1), readOnly: true)
                .Out<long>("dst", shape.Rows, OutputPolicy.Exact),
            shapes: s_shapes,
            launchExpression: launchExpression);

    internal static readonly DifferentialCase s_nintAdd = Binary(
        NativeIntegerOperatorKernels.LaunchNintAddKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Add_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintAdd);

    internal static readonly DifferentialCase s_nintSubtract = Binary(
        NativeIntegerOperatorKernels.LaunchNintSubtractKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Subtract_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintSubtract);

    internal static readonly DifferentialCase s_nintMultiply = Binary(
        NativeIntegerOperatorKernels.LaunchNintMultiplyKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Multiply_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintMultiply);

    internal static readonly DifferentialCase s_nintAnd = Binary(
        NativeIntegerOperatorKernels.LaunchNintAndKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_And_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintAnd);

    internal static readonly DifferentialCase s_nintOr = Binary(
        NativeIntegerOperatorKernels.LaunchNintOrKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Or_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintOr);

    internal static readonly DifferentialCase s_nintXor = Binary(
        NativeIntegerOperatorKernels.LaunchNintXorKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Xor_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintXor);

    internal static readonly DifferentialCase s_nintLessThan = Binary(
        NativeIntegerOperatorKernels.LaunchNintLessThanKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_LessThan_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintLessThan);

    internal static readonly DifferentialCase s_nintDivide = Binary(
        NativeIntegerOperatorKernels.LaunchNintDivideKernel, "sdiv",
        static (a, b) => b == -1 ? unchecked(-a) : a / b);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Divide_MatchesTheGpuResultOnEveryPath() =>
        DifferentialRunner.Run(s_nintDivide);

    internal static readonly DifferentialCase s_nintDivideByZero = Binary(
        NativeIntegerOperatorKernels.LaunchNintDivideKernel, "sdivzero");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_DivideByZero_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintDivideByZero);

    internal static readonly DifferentialCase s_nintRemainder = Binary(
        NativeIntegerOperatorKernels.LaunchNintRemainderKernel, "sdiv",
        static (a, b) => b == -1 ? 0 : a % b);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Remainder_MatchesTheGpuResultOnEveryPath() =>
        DifferentialRunner.Run(s_nintRemainder);

    internal static readonly DifferentialCase s_nintRemainderByZero = Binary(
        NativeIntegerOperatorKernels.LaunchNintRemainderKernel, "sdivzero");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_RemainderByZero_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintRemainderByZero);

    internal static readonly DifferentialCase s_nintShiftLeft = Binary(
        NativeIntegerOperatorKernels.LaunchNintShiftLeftKernel, "shift");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_ShiftLeft_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintShiftLeft);

    internal static readonly DifferentialCase s_nintShiftRight = Binary(
        NativeIntegerOperatorKernels.LaunchNintShiftRightKernel, "shift");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_ShiftRight_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintShiftRight);

    internal static readonly DifferentialCase s_nintUnsignedShiftRight = Binary(
        NativeIntegerOperatorKernels.LaunchNintUnsignedShiftRightKernel, "shift");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_UnsignedShiftRight_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintUnsignedShiftRight);

    internal static readonly DifferentialCase s_nintNegate = Unary(
        NativeIntegerOperatorKernels.LaunchNintNegateKernel);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Negate_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintNegate);

    internal static readonly DifferentialCase s_nintComplement = Unary(
        NativeIntegerOperatorKernels.LaunchNintComplementKernel);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Complement_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintComplement);

    internal static readonly DifferentialCase s_nintIncrement = Unary(
        NativeIntegerOperatorKernels.LaunchNintIncrementKernel);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Increment_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintIncrement);

    internal static readonly DifferentialCase s_nintDecrement = Unary(
        NativeIntegerOperatorKernels.LaunchNintDecrementKernel);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_Decrement_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintDecrement);

    internal static readonly DifferentialCase s_nintAddAssign = Binary(
        NativeIntegerOperatorKernels.LaunchNintAddAssignKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_AddAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintAddAssign);

    internal static readonly DifferentialCase s_nintSubtractAssign = Binary(
        NativeIntegerOperatorKernels.LaunchNintSubtractAssignKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_SubtractAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintSubtractAssign);

    internal static readonly DifferentialCase s_nintMultiplyAssign = Binary(
        NativeIntegerOperatorKernels.LaunchNintMultiplyAssignKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_MultiplyAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintMultiplyAssign);

    internal static readonly DifferentialCase s_nintDivideAssign = Binary(
        NativeIntegerOperatorKernels.LaunchNintDivideAssignKernel, "sdiv",
        static (a, b) => b == -1 ? unchecked(-a) : a / b);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_DivideAssign_MatchesTheGpuResultOnEveryPath() =>
        DifferentialRunner.Run(s_nintDivideAssign);

    internal static readonly DifferentialCase s_nintDivideAssignByZero = Binary(
        NativeIntegerOperatorKernels.LaunchNintDivideAssignKernel, "sdivzero");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_DivideAssignByZero_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintDivideAssignByZero);

    internal static readonly DifferentialCase s_nintRemainderAssign = Binary(
        NativeIntegerOperatorKernels.LaunchNintRemainderAssignKernel, "sdiv",
        static (a, b) => b == -1 ? 0 : a % b);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_RemainderAssign_MatchesTheGpuResultOnEveryPath() =>
        DifferentialRunner.Run(s_nintRemainderAssign);

    internal static readonly DifferentialCase s_nintRemainderAssignByZero = Binary(
        NativeIntegerOperatorKernels.LaunchNintRemainderAssignKernel, "sdivzero");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_RemainderAssignByZero_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintRemainderAssignByZero);

    internal static readonly DifferentialCase s_nintShiftLeftAssign = Binary(
        NativeIntegerOperatorKernels.LaunchNintShiftLeftAssignKernel, "shift");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_ShiftLeftAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintShiftLeftAssign);

    internal static readonly DifferentialCase s_nintShiftRightAssign = Binary(
        NativeIntegerOperatorKernels.LaunchNintShiftRightAssignKernel, "shift");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_ShiftRightAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintShiftRightAssign);

    internal static readonly DifferentialCase s_nintUnsignedShiftRightAssign = Binary(
        NativeIntegerOperatorKernels.LaunchNintUnsignedShiftRightAssignKernel, "shift");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nint_UnsignedShiftRightAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintUnsignedShiftRightAssign);

    internal static readonly DifferentialCase s_nuintAdd = Binary(
        NativeIntegerOperatorKernels.LaunchNuintAddKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_Add_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintAdd);

    internal static readonly DifferentialCase s_nuintSubtract = Binary(
        NativeIntegerOperatorKernels.LaunchNuintSubtractKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_Subtract_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintSubtract);

    internal static readonly DifferentialCase s_nuintMultiply = Binary(
        NativeIntegerOperatorKernels.LaunchNuintMultiplyKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_Multiply_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintMultiply);

    internal static readonly DifferentialCase s_nuintLessThan = Binary(
        NativeIntegerOperatorKernels.LaunchNuintLessThanKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_LessThan_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintLessThan);

    internal static readonly DifferentialCase s_nuintDivide = Binary(
        NativeIntegerOperatorKernels.LaunchNuintDivideKernel, "div");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_Divide_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintDivide);

    internal static readonly DifferentialCase s_nuintRemainder = Binary(
        NativeIntegerOperatorKernels.LaunchNuintRemainderKernel, "div");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_Remainder_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintRemainder);

    internal static readonly DifferentialCase s_nuintShiftLeft = Binary(
        NativeIntegerOperatorKernels.LaunchNuintShiftLeftKernel, "shift");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_ShiftLeft_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintShiftLeft);

    internal static readonly DifferentialCase s_nuintShiftRight = Binary(
        NativeIntegerOperatorKernels.LaunchNuintShiftRightKernel, "shift");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_ShiftRight_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintShiftRight);

    internal static readonly DifferentialCase s_nuintUnsignedShiftRight = Binary(
        NativeIntegerOperatorKernels.LaunchNuintUnsignedShiftRightKernel, "shift");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_UnsignedShiftRight_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintUnsignedShiftRight);

    internal static readonly DifferentialCase s_nuintComplement = Unary(
        NativeIntegerOperatorKernels.LaunchNuintComplementKernel);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_Complement_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintComplement);

    internal static readonly DifferentialCase s_nuintAddAssign = Binary(
        NativeIntegerOperatorKernels.LaunchNuintAddAssignKernel, "pair");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_AddAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintAddAssign);

    internal static readonly DifferentialCase s_nuintDivideAssign = Binary(
        NativeIntegerOperatorKernels.LaunchNuintDivideAssignKernel, "div");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_DivideAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintDivideAssign);

    internal static readonly DifferentialCase s_nuintShiftLeftAssign = Binary(
        NativeIntegerOperatorKernels.LaunchNuintShiftLeftAssignKernel, "shift");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_ShiftLeftAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintShiftLeftAssign);

    internal static readonly DifferentialCase s_nuintShiftRightAssign = Binary(
        NativeIntegerOperatorKernels.LaunchNuintShiftRightAssignKernel, "shift");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_ShiftRightAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintShiftRightAssign);

    internal static readonly DifferentialCase s_nuintUnsignedShiftRightAssign = Binary(
        NativeIntegerOperatorKernels.LaunchNuintUnsignedShiftRightAssignKernel, "shift");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_UnsignedShiftRightAssign_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintUnsignedShiftRightAssign);

    internal static readonly DifferentialCase s_nuintLeadingZeroCount = Unary(
        NativeIntegerOperatorKernels.LaunchNuintLeadingZeroCountKernel);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_BitOperationsLeadingZeroCount_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintLeadingZeroCount);

    internal static readonly DifferentialCase s_nuintTrailingZeroCount = Unary(
        NativeIntegerOperatorKernels.LaunchNuintTrailingZeroCountKernel);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_BitOperationsTrailingZeroCount_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintTrailingZeroCount);

    internal static readonly DifferentialCase s_nuintPopCount = Unary(
        NativeIntegerOperatorKernels.LaunchNuintPopCountKernel);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_BitOperationsPopCount_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintPopCount);

    internal static readonly DifferentialCase s_nuintLog2 = Unary(
        NativeIntegerOperatorKernels.LaunchNuintLog2Kernel);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_BitOperationsLog2_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintLog2);

    internal static readonly DifferentialCase s_nuintRotateLeft = DiffCases.CreateRows<long, int, long>(
        NativeIntegerOperatorKernels.LaunchNuintRotateLeftKernel,
        inputs: static (_, shape) => new DiffBuffers()
            .In("a", Cycle(s_edges, shape.Rows, 1), readOnly: true)
            .In("counts", Cycle(s_rotateCounts, shape.Rows, s_edges.Length), readOnly: true)
            .Out<long>("dst", shape.Rows, OutputPolicy.Exact),
        shapes: s_shapes,
        firstInputName: "a",
        secondInputName: "counts",
        outputName: "dst");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_BitOperationsRotateLeft_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintRotateLeft);

    internal static readonly DifferentialCase s_nuintRotateRight = DiffCases.CreateRows<long, int, long>(
        NativeIntegerOperatorKernels.LaunchNuintRotateRightKernel,
        inputs: static (_, shape) => new DiffBuffers()
            .In("a", Cycle(s_edges, shape.Rows, 1), readOnly: true)
            .In("counts", Cycle(s_rotateCounts, shape.Rows, s_edges.Length), readOnly: true)
            .Out<long>("dst", shape.Rows, OutputPolicy.Exact),
        shapes: s_shapes,
        firstInputName: "a",
        secondInputName: "counts",
        outputName: "dst");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Nuint_BitOperationsRotateRight_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintRotateRight);
}
