#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests (<c>struct</c> value semantics, defensive copies, constructors,
/// field initializers, <c>with</c> expressions, and user-defined conversions).
/// </summary>
public class StructDifferentialTests
{
    private const int Rows = 441;

    internal static readonly DifferentialCase s_mutationAndCopy = DiffCases.Create(
        StructDifferentialKernels.LaunchStructMutationAndCopySemantics,
        inputs: static (rng, rows) =>
        {
            var (inputs, deltas) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .In("deltas", deltas)
                .Out<int>("r", rows, StructDifferentialKernels.MutationAndCopyColumns, OutputPolicy.Exact);
        },
        shapes: LaunchShapes.OneDimensionalMatrix(Rows),
        firstInputName: "inputs",
        secondInputName: "deltas",
        outputName: "r");

    internal static readonly DifferentialCase s_defaultVsNew = DiffCases.Create(
        StructDifferentialKernels.LaunchStructDefaultVsNewAndFieldInitializers,
        inputs: static (rng, rows) =>
        {
            var (inputs, flags) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .In("flags", flags)
                .Out<int>("r", rows, StructDifferentialKernels.DefaultVsNewColumns, OutputPolicy.Exact);
        },
        shapes: LaunchShapes.OneDimensionalMatrix(Rows),
        firstInputName: "inputs",
        secondInputName: "flags",
        outputName: "r");

    internal static readonly DifferentialCase s_recordWith = DiffCases.Create(
        StructDifferentialKernels.LaunchRecordStructWithExpressions,
        inputs: static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            var zs = DiffValues.Int32(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .In("zs", zs)
                .Out<int>("r", rows, StructDifferentialKernels.RecordWithColumns, OutputPolicy.Exact);
        },
        shapes: LaunchShapes.OneDimensionalMatrix(Rows),
        firstName: "xs",
        secondName: "ys",
        thirdName: "zs",
        fourthName: "r");

    internal static readonly DifferentialCase s_autoPropInDefensiveCopy = DiffCases.Create(
        StructDifferentialKernels.LaunchAutoPropertyPassedToInParameterDefensiveCopy,
        inputs: static (rng, rows) =>
        {
            var (initialValues, mutatedValues) = DiffValues.Int32Pairs(rng, rows);
            for (int k = 0; k < rows; k++)
            {
                if (initialValues[k] == mutatedValues[k])
                {
                    mutatedValues[k] = unchecked(initialValues[k] + 1);
                }
            }

            return new DiffBuffers()
                .In("initialValues", initialValues)
                .In("mutatedValues", mutatedValues)
                .Out<int>("r", rows, StructDifferentialKernels.AutoPropInDefensiveCopyColumns, OutputPolicy.Exact);
        },
        shapes: LaunchShapes.OneDimensionalMatrix(Rows),
        firstInputName: "initialValues",
        secondInputName: "mutatedValues",
        outputName: "r");

    internal static readonly DifferentialCase s_userDefinedImplicitConv = DiffCases.Create(
        StructDifferentialKernels.LaunchStructUserDefinedImplicitConversion,
        inputs: static (rng, rows) =>
        {
            var inputs = DiffValues.Int32(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .Out<int>("r", rows, StructDifferentialKernels.UserDefinedImplicitConvColumns, OutputPolicy.Exact);
        },
        shapes: LaunchShapes.OneDimensionalMatrix(Rows),
        inputName: "inputs",
        outputName: "r");

    internal static readonly DifferentialCase s_userDefinedExplicitConv = DiffCases.Create(
        StructDifferentialKernels.LaunchStructUserDefinedExplicitConversion,
        inputs: static (rng, rows) =>
        {
            var inputs = DiffValues.Int32(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .Out<int>("r", rows, StructDifferentialKernels.UserDefinedExplicitConvColumns, OutputPolicy.Exact);
        },
        shapes: LaunchShapes.OneDimensionalMatrix(Rows),
        inputName: "inputs",
        outputName: "r");

    internal static readonly DifferentialCase s_liftedUserDefinedConv = DiffCases.Create(
        StructDifferentialKernels.LaunchStructLiftedUserDefinedConversion,
        inputs: static (rng, rows) =>
        {
            var inputs = DiffValues.Int32(rng, rows);
            var flags = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                flags[k] = rng.IsBoundarySeed ? (k & 1) : (rng.NextBool() ? 1 : 0);
            }

            return new DiffBuffers()
                .In("inputs", inputs)
                .In("flags", flags)
                .Out<int>("r", rows, StructDifferentialKernels.LiftedImplicitConvColumns, OutputPolicy.Exact);
        },
        shapes: LaunchShapes.OneDimensionalMatrix(Rows),
        firstInputName: "inputs",
        secondInputName: "flags",
        outputName: "r");

    internal static readonly DifferentialCase s_repro1103Default = DiffCases.Create(
        StructDifferentialKernels.LaunchDefaultOfConvertingStructReproKernel,
        inputs: static (rng, rows) =>
        {
            var inputs = DiffValues.Int32(rng, rows);
            return new DiffBuffers()
                .In("a", inputs)
                .Out<int>("dst", rows, StructDifferentialKernels.Repro1103Columns, OutputPolicy.Exact);
        },
        shapes: LaunchShapes.OneDimensionalMatrix(Rows));

    internal static readonly DifferentialCase s_constantSizedFixedBuffer = DiffCases.Create(
        StructDifferentialKernels.LaunchConstantSizedFixedBufferKernel,
        inputs: static (rng, rows) =>
        {
            var inputs = DiffValues.Int32(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .Out<int>("r", rows, StructDifferentialKernels.ConstantSizedFixedBufferColumns, OutputPolicy.Exact);
        },
        shapes: LaunchShapes.OneDimensionalMatrix(Rows),
        inputName: "inputs",
        outputName: "r");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void StructSemantics_MutationAndCopy_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mutationAndCopy);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void StructSemantics_DefaultVsNewAndFieldInitializers_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_defaultVsNew);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void StructSemantics_RecordStructWithExpressions_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_recordWith);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void StructSemantics_AutoPropertyInParamDefensiveCopy_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_autoPropInDefensiveCopy);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void StructSemantics_UserDefinedImplicitConversion_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_userDefinedImplicitConv);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void StructSemantics_UserDefinedExplicitConversion_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_userDefinedExplicitConv);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void StructSemantics_LiftedUserDefinedConversion_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_liftedUserDefinedConv);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void StructSemantics_DefaultOfConvertingStruct_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_repro1103Default);

    // A fixed buffer sized by a constant of a struct declared in another file (#1283)
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void StructSemantics_ConstantSizedFixedBufferAcrossFiles_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_constantSizedFixedBuffer);
}
