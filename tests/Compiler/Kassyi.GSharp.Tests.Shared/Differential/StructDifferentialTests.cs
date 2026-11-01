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

    private static readonly DifferentialCase s_mutationAndCopy = new(
        $"{nameof(StructDifferentialKernels)}.{nameof(StructDifferentialKernels.StructMutationAndCopySemantics)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (inputs, deltas) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .In("deltas", deltas)
                .Out<int>("r", rows, StructDifferentialKernels.MutationAndCopyColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => StructDifferentialKernels.LaunchStructMutationAndCopySemantics(
            b.Get<int>("inputs"),
            b.Get<int>("deltas"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => StructDifferentialKernels.StructMutationAndCopySemantics(
            b.Get<int>("inputs"),
            b.Get<int>("deltas"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_defaultVsNew = new(
        $"{nameof(StructDifferentialKernels)}.{nameof(StructDifferentialKernels.StructDefaultVsNewAndFieldInitializers)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (inputs, flags) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .In("flags", flags)
                .Out<int>("r", rows, StructDifferentialKernels.DefaultVsNewColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => StructDifferentialKernels.LaunchStructDefaultVsNewAndFieldInitializers(
            b.Get<int>("inputs"),
            b.Get<int>("flags"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => StructDifferentialKernels.StructDefaultVsNewAndFieldInitializers(
            b.Get<int>("inputs"),
            b.Get<int>("flags"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_recordWith = new(
        $"{nameof(StructDifferentialKernels)}.{nameof(StructDifferentialKernels.RecordStructWithExpressions)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            var zs = DiffValues.Int32(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .In("zs", zs)
                .Out<int>("r", rows, StructDifferentialKernels.RecordWithColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => StructDifferentialKernels.LaunchRecordStructWithExpressions(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("zs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => StructDifferentialKernels.RecordStructWithExpressions(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("zs"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_autoPropInDefensiveCopy = new(
        $"{nameof(StructDifferentialKernels)}.{nameof(StructDifferentialKernels.AutoPropertyPassedToInParameterDefensiveCopy)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
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
        Launch = static (b, s) => StructDifferentialKernels.LaunchAutoPropertyPassedToInParameterDefensiveCopy(
            b.Get<int>("initialValues"),
            b.Get<int>("mutatedValues"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => StructDifferentialKernels.AutoPropertyPassedToInParameterDefensiveCopy(
            b.Get<int>("initialValues"),
            b.Get<int>("mutatedValues"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_userDefinedImplicitConv = new(
        $"{nameof(StructDifferentialKernels)}.{nameof(StructDifferentialKernels.StructUserDefinedImplicitConversion)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var inputs = DiffValues.Int32(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .Out<int>("r", rows, StructDifferentialKernels.UserDefinedImplicitConvColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => StructDifferentialKernels.LaunchStructUserDefinedImplicitConversion(
            b.Get<int>("inputs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => StructDifferentialKernels.StructUserDefinedImplicitConversion(
            b.Get<int>("inputs"),
            b.Get<int>("r")),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_StructMutationAndCopySemantics_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mutationAndCopy);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_StructDefaultVsNewAndFieldInitializers_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_defaultVsNew);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_RecordStructWithExpressions_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_recordWith);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_AutoPropertyPassedToInParameterDefensiveCopy_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_autoPropInDefensiveCopy);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_StructUserDefinedImplicitConversion_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_userDefinedImplicitConv);
}
