#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests (<c>struct</c> value semantics, defensive copies, constructors,
/// field initializers, <c>with</c> expressions, and user-defined conversions).
/// </summary>
public class M1DiffStructTests
{
    private const int Rows = 441;

    private static readonly DifferentialCase s_mutationAndCopy = new(
        $"{nameof(M1DiffStructKernels)}.{nameof(M1DiffStructKernels.StructMutationAndCopySemantics)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (inputs, deltas) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .In("deltas", deltas)
                .Out<int>("r", rows, M1DiffStructKernels.MutationAndCopyColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffStructKernels.LaunchStructMutationAndCopySemantics(
            b.Get<int>("inputs"),
            b.Get<int>("deltas"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => M1DiffStructKernels.StructMutationAndCopySemantics(
            b.Get<int>("inputs"),
            b.Get<int>("deltas"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_defaultVsNew = new(
        $"{nameof(M1DiffStructKernels)}.{nameof(M1DiffStructKernels.StructDefaultVsNewAndFieldInitializers)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (inputs, flags) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .In("flags", flags)
                .Out<int>("r", rows, M1DiffStructKernels.DefaultVsNewColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffStructKernels.LaunchStructDefaultVsNewAndFieldInitializers(
            b.Get<int>("inputs"),
            b.Get<int>("flags"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => M1DiffStructKernels.StructDefaultVsNewAndFieldInitializers(
            b.Get<int>("inputs"),
            b.Get<int>("flags"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_recordWith = new(
        $"{nameof(M1DiffStructKernels)}.{nameof(M1DiffStructKernels.RecordStructWithExpressions)}")
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
                .Out<int>("r", rows, M1DiffStructKernels.RecordWithColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffStructKernels.LaunchRecordStructWithExpressions(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("zs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => M1DiffStructKernels.RecordStructWithExpressions(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("zs"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_autoPropInDefensiveCopy = new(
        $"{nameof(M1DiffStructKernels)}.{nameof(M1DiffStructKernels.AutoPropertyPassedToInParameterDefensiveCopy)}")
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
                .Out<int>("r", rows, M1DiffStructKernels.AutoPropInDefensiveCopyColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffStructKernels.LaunchAutoPropertyPassedToInParameterDefensiveCopy(
            b.Get<int>("initialValues"),
            b.Get<int>("mutatedValues"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => M1DiffStructKernels.AutoPropertyPassedToInParameterDefensiveCopy(
            b.Get<int>("initialValues"),
            b.Get<int>("mutatedValues"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_userDefinedImplicitConv = new(
        $"{nameof(M1DiffStructKernels)}.{nameof(M1DiffStructKernels.StructUserDefinedImplicitConversion)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var inputs = DiffValues.Int32(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .Out<int>("r", rows, M1DiffStructKernels.UserDefinedImplicitConvColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffStructKernels.LaunchStructUserDefinedImplicitConversion(
            b.Get<int>("inputs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => M1DiffStructKernels.StructUserDefinedImplicitConversion(
            b.Get<int>("inputs"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_userDefinedExplicitConv = new(
        $"{nameof(M1DiffStructKernels)}.{nameof(M1DiffStructKernels.StructUserDefinedExplicitConversion)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var inputs = DiffValues.Int32(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .Out<int>("r", rows, M1DiffStructKernels.UserDefinedExplicitConvColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffStructKernels.LaunchStructUserDefinedExplicitConversion(
            b.Get<int>("inputs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => M1DiffStructKernels.StructUserDefinedExplicitConversion(
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

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_StructUserDefinedExplicitConversion_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_userDefinedExplicitConv);
}
