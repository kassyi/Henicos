#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for the calls that C# makes without a call in the source: a collection expression of a
/// <c>[CollectionBuilder]</c> type passes its elements as a span to the builder, and a <c>fixed</c> statement over a
/// struct pins what its extension <c>GetPinnableReference</c> returns (#1726).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.HappyPath)]
public class ImplicitCallDifferentialTests
{
    private const int Rows = 128;

    internal static readonly DifferentialCase s_collectionBuilder = DiffCases.Create(
        ImplicitCallDifferentialKernels.LaunchCollectionBuilderKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, ImplicitCallDifferentialKernels.BuilderColumns, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        inputName: "inputs",
        outputName: "r");

    internal static readonly DifferentialCase s_pinnableReference = DiffCases.Create(
        ImplicitCallDifferentialKernels.LaunchPinnableReferenceKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, ImplicitCallDifferentialKernels.PinnableColumns, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        inputName: "inputs",
        outputName: "r");

    [SkippableFact]
    public void CollectionExpression_CollectionBuilderType_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_collectionBuilder);

    [SkippableFact]
    public void FixedStatement_ExtensionGetPinnableReference_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_pinnableReference);
}
