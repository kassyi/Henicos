#nullable enable
using System.Runtime.CompilerServices;
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

    private static DifferentialCase Case(
        Int32RowsLaunch launch,
        string[] columns,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, columns, OutputPolicy.Exact),
            shapes: LaunchShapes.OneDimensionalMatrix(Rows),
            launchExpression: launchExpression);

    internal static readonly DifferentialCase s_var = Case(
        DefaultValueDifferentialKernels.LaunchDefaultVarKernel,
        ["int", "long", "uint", "ulong"]);

    internal static readonly DifferentialCase s_conditionalArm = Case(
        DefaultValueDifferentialKernels.LaunchDefaultConditionalArmKernel,
        ["int_arm", "uint_arm", "long_arm"]);

    internal static readonly DifferentialCase s_narrowTypes = Case(
        DefaultValueDifferentialKernels.LaunchDefaultNarrowTypesKernel,
        ["byte", "sbyte", "short", "ushort", "byte_arm", "enum_arm", "enum_var"]);

    internal static readonly DifferentialCase s_comparisonAndOperand = Case(
        DefaultValueDifferentialKernels.LaunchDefaultComparisonAndOperandKernel,
        ["equal", "not_equal", "long_operand"]);

    internal static readonly DifferentialCase s_return = Case(
        DefaultValueDifferentialKernels.LaunchDefaultReturnKernel,
        ["long_return", "uint_return"]);

    internal static readonly DifferentialCase s_struct = Case(
        DefaultValueDifferentialKernels.LaunchDefaultStructKernel,
        ["struct_arm_tag", "field_has_value", "default_tag"]);

    internal static readonly DifferentialCase s_zeroStructBesideAValue = Case(
        DefaultValueDifferentialKernels.LaunchZeroStructBesideAValueKernel,
        ["created_arm_tag", "switch_default_arm_tag", "get_value_or_default_tag"]);

    internal static readonly DifferentialCase s_nullable = Case(
        DefaultValueDifferentialKernels.LaunchDefaultNullableKernel,
        ["maybe_has_value", "q_has_value", "equals_default", "not_equals_default", "default_on_left"]);

    internal static readonly DifferentialCase s_array = Case(
        DefaultValueDifferentialKernels.LaunchDefaultArrayKernel,
        ["arm_is_null", "equals_default", "other_equals_default"]);

    internal static readonly DifferentialCase s_pointer = Case(
        DefaultValueDifferentialKernels.LaunchDefaultPointerKernel,
        ["pointer_or_value"]);

    internal static readonly DifferentialCase s_convertedToNullable = Case(
        DefaultValueDifferentialKernels.LaunchDefaultConvertedToNullableKernel,
        ["declaration", "assignment", "widened", "literal"]);

    internal static readonly DifferentialCase s_omittedIn = Case(
        DefaultValueDifferentialKernels.LaunchDefaultOmittedInArgumentKernel,
        ["omitted_default", "given", "omitted_constant"]);

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
    public void ZeroValue_OfAStructWithNullableFieldsBesideAValue_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_zeroStructBesideAValue);

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
