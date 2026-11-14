#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests of <c>default(T)</c> and the <c>default</c> literal in expression positions (<c>var</c>, a
/// conditional arm, a comparison, an operand, a return value, a struct, a nullable, an array and a pointer), one case
/// per shape.
/// </summary>
public class DefaultValueDifferentialTests
{
    private const int Rows = 64;

    private static DifferentialCase Case(string kernel, string[] columns, Action<DiffBuffers, LaunchShape> launch) =>
        new($"{nameof(DefaultValueDifferentialKernels)}.{kernel}")
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
            Inputs = (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, columns, OutputPolicy.Exact),
            Launch = launch,
            OracleIsLaunch = true,
        };

    internal static readonly DifferentialCase s_var = Case(
        nameof(DefaultValueDifferentialKernels.DefaultVarKernel),
        ["int", "long", "uint", "ulong"],
        static (b, s) => DefaultValueDifferentialKernels.LaunchDefaultVarKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_conditionalArm = Case(
        nameof(DefaultValueDifferentialKernels.DefaultConditionalArmKernel),
        ["int_arm", "uint_arm", "long_arm"],
        static (b, s) => DefaultValueDifferentialKernels.LaunchDefaultConditionalArmKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_narrowTypes = Case(
        nameof(DefaultValueDifferentialKernels.DefaultNarrowTypesKernel),
        ["byte", "sbyte", "short", "ushort", "byte_arm", "enum_arm", "enum_var"],
        static (b, s) => DefaultValueDifferentialKernels.LaunchDefaultNarrowTypesKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_comparisonAndOperand = Case(
        nameof(DefaultValueDifferentialKernels.DefaultComparisonAndOperandKernel),
        ["equal", "not_equal", "long_operand"],
        static (b, s) => DefaultValueDifferentialKernels.LaunchDefaultComparisonAndOperandKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_return = Case(
        nameof(DefaultValueDifferentialKernels.DefaultReturnKernel),
        ["long_return", "uint_return"],
        static (b, s) => DefaultValueDifferentialKernels.LaunchDefaultReturnKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_struct = Case(
        nameof(DefaultValueDifferentialKernels.DefaultStructKernel),
        ["struct_arm_tag", "field_has_value", "default_tag"],
        static (b, s) => DefaultValueDifferentialKernels.LaunchDefaultStructKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_nullable = Case(
        nameof(DefaultValueDifferentialKernels.DefaultNullableKernel),
        ["maybe_has_value", "q_has_value", "equals_default", "not_equals_default", "default_on_left"],
        static (b, s) => DefaultValueDifferentialKernels.LaunchDefaultNullableKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_array = Case(
        nameof(DefaultValueDifferentialKernels.DefaultArrayKernel),
        ["arm_is_null", "equals_default", "other_equals_default"],
        static (b, s) => DefaultValueDifferentialKernels.LaunchDefaultArrayKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_pointer = Case(
        nameof(DefaultValueDifferentialKernels.DefaultPointerKernel),
        ["pointer_or_value"],
        static (b, s) => DefaultValueDifferentialKernels.LaunchDefaultPointerKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_convertedToNullable = Case(
        nameof(DefaultValueDifferentialKernels.DefaultConvertedToNullableKernel),
        ["declaration", "assignment", "widened", "literal"],
        static (b, s) => DefaultValueDifferentialKernels.LaunchDefaultConvertedToNullableKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_omittedIn = Case(
        nameof(DefaultValueDifferentialKernels.DefaultOmittedInArgumentKernel),
        ["omitted_default", "given", "omitted_constant"],
        static (b, s) => DefaultValueDifferentialKernels.LaunchDefaultOmittedInArgumentKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void DefaultValue_InVarDeclarations_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_var);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void DefaultValue_InConditionalArms_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_conditionalArm);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void DefaultValue_OfNarrowIntegerAndEnumTypes_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_narrowTypes);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void DefaultValue_InComparisonsAndOperands_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_comparisonAndOperand);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void DefaultValue_AsReturnValue_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_return);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void DefaultValue_OfAStruct_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_struct);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void DefaultValue_OfANullableAndItsComparison_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullable);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void DefaultValue_OfAnArray_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_array);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void DefaultValue_OfAPointer_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_pointer);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void DefaultValue_OfTheUnderlyingTypeConvertedToANullable_HasAValue() =>
        DifferentialRunner.Run(s_convertedToNullable);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void DefaultValue_AsAnOmittedInArgument_PassesTheAddressOfATemporary() =>
        DifferentialRunner.Run(s_omittedIn);
}
