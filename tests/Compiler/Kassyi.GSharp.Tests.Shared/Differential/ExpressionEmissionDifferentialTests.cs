#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <see cref="ExpressionEmissionDifferentialKernels"/>: switch expressions whose arms have
/// different types or whose input is floating-point, the null-forgiving operator and <c>??</c> / <c>??=</c> on arrays,
/// implicitly converted constructor arguments and array elements, and nested member initializers.
/// </summary>
public sealed class ExpressionEmissionDifferentialTests
{
    private const int Rows = 64;

    // Values that the switch arms name, their neighbours, and the values C# equality treats specially
    private static readonly double[] s_doubleCases =
    [
        0.5, 1.5, 0.0, -0.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue,
        double.Epsilon, -0.5, 0.25, 1.0, -double.Epsilon, double.MinValue, BitConverter.Int64BitsToDouble(0x7FF8_0000_0000_0001),
        -1.5
    ];

    private static readonly float[] s_singleCases =
    [
        0.5f, -2.5f, 0f, -0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MinValue, float.MaxValue,
        float.Epsilon, 2.5f, -0.5f
    ];

    private static readonly int[] s_conversionCases =
    [
        0, 1, -1, 2, 3, 16_777_217, -16_777_217, int.MaxValue, int.MinValue, 0x7F, 0x80, 0xFF, 0x7FFF, 0x8000, 0xFFFF,
        0x1_0000, 123_456_789
    ];

    internal static readonly DifferentialCase s_switchArmConversion = new(
        $"{nameof(ExpressionEmissionDifferentialKernels)}.{nameof(ExpressionEmissionDifferentialKernels.SwitchArmConversionKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Mixed(rng, rows, s_conversionCases, DiffValues.NextInt32))
            .Out<long>("dst", rows, ["int_long_arms", "uint_int_arms", "nested_narrow_arms", "arms_in_operand"], OutputPolicy.Bitwise)
            .Out<float>("fdst", rows, ["int_float_long_arms", "int_double_arms", "char_float_arms", "narrow_nan_arms"], OutputPolicy.Bitwise),
        Launch = static (b, s) => ExpressionEmissionDifferentialKernels.LaunchSwitchArmConversionKernel(
            b.Get<int>("a"), b.Get<long>("dst"), b.Get<float>("fdst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_floatingSwitch = new(
        $"{nameof(ExpressionEmissionDifferentialKernels)}.{nameof(ExpressionEmissionDifferentialKernels.FloatingSwitchKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("d", DiffValues.Cycled(rng, rows, s_doubleCases,
                static r => BitConverter.Int64BitsToDouble(unchecked((long)r.NextUInt64()))))
            .In("f", DiffValues.Cycled(rng, rows, s_singleCases, DiffValues.NextSingle))
            .Out<int>("dst", rows, ["double_arms", "float_arms", "evaluated_once", "nested_floating"], OutputPolicy.Bitwise),
        Launch = static (b, s) => ExpressionEmissionDifferentialKernels.LaunchFloatingSwitchKernel(
            b.Get<double>("d"), b.Get<float>("f"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_nullableSwitch = new(
        $"{nameof(ExpressionEmissionDifferentialKernels)}.{nameof(ExpressionEmissionDifferentialKernels.NullableSwitchKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .In("d", DiffValues.Cycled(rng, rows, s_doubleCases,
                static r => BitConverter.Int64BitsToDouble(unchecked((long)r.NextUInt64()))))
            .Out<int>("dst", rows, ["null_arms", "nullable_int_input", "nullable_double_input", "array_null_arm"], OutputPolicy.Bitwise),
        Launch = static (b, s) => ExpressionEmissionDifferentialKernels.LaunchNullableSwitchKernel(
            b.Get<int>("a"), b.Get<double>("d"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_nullForgivingAndArrayCoalesce = new(
        $"{nameof(ExpressionEmissionDifferentialKernels)}.{nameof(ExpressionEmissionDifferentialKernels.NullForgivingAndArrayCoalesceKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .In("b", DiffValues.Int32(rng, rows))
            .Out<int>("dst", rows, ["null_forgiving", "coalesce", "coalesce_assign", "coalesce_operand"], OutputPolicy.Bitwise),
        Launch = static (b, s) => ExpressionEmissionDifferentialKernels.LaunchNullForgivingAndArrayCoalesceKernel(
            b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_implicitArgumentConversion = new(
        $"{nameof(ExpressionEmissionDifferentialKernels)}.{nameof(ExpressionEmissionDifferentialKernels.ImplicitArgumentConversionKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Mixed(rng, rows, s_conversionCases, DiffValues.NextInt32))
            .In("la", DiffValues.Random(rng, rows, static r => unchecked((long)r.NextUInt64()) >> (int)(r.NextUInt32() & 63)))
            .Out<float>("dst", rows, ["vector2", "vector3", "vector4", "array_initializer"], OutputPolicy.Bitwise)
            .Out<double>("ddst", rows, ["nullable_int", "nullable_long", "vector2_long", "initializer_long"], OutputPolicy.Bitwise),
        Launch = static (b, s) => ExpressionEmissionDifferentialKernels.LaunchImplicitArgumentConversionKernel(
            b.Get<int>("a"), b.Get<long>("la"), b.Get<float>("dst"), b.Get<double>("ddst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_nestedMemberInitializer = new(
        $"{nameof(ExpressionEmissionDifferentialKernels)}.{nameof(ExpressionEmissionDifferentialKernels.NestedMemberInitializerKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .Out<int>("dst", rows, ["nested_member", "nested_order", "two_levels", "after_constructor"], OutputPolicy.Bitwise),
        Launch = static (b, s) => ExpressionEmissionDifferentialKernels.LaunchNestedMemberInitializerKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SwitchExpression_ArmsOfDifferentTypes_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_switchArmConversion);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SwitchExpression_FloatingPointInput_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_floatingSwitch);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SwitchExpression_NullArmsAndNullableInput_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableSwitch);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ExpressionEmission_NullForgivingAndArrayCoalesce_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullForgivingAndArrayCoalesce);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ExpressionEmission_ImplicitlyConvertedBraceArguments_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_implicitArgumentConversion);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void MemberInitializer_NestedMembers_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nestedMemberInitializer);
}
