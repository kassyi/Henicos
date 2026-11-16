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

    private const string Kernels = nameof(ImplicitCallDifferentialKernels);

    internal static readonly DifferentialCase s_collectionBuilder = new(
        Kernels + "." + nameof(ImplicitCallDifferentialKernels.CollectionBuilderKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, ImplicitCallDifferentialKernels.BuilderColumns, OutputPolicy.Exact),
        Launch = static (b, s) => ImplicitCallDifferentialKernels.LaunchCollectionBuilderKernel(
            b.Get<int>("inputs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_pinnableReference = new(
        Kernels + "." + nameof(ImplicitCallDifferentialKernels.PinnableReferenceKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, ImplicitCallDifferentialKernels.PinnableColumns, OutputPolicy.Exact),
        Launch = static (b, s) => ImplicitCallDifferentialKernels.LaunchPinnableReferenceKernel(
            b.Get<int>("inputs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    public void CollectionExpression_CollectionBuilderType_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_collectionBuilder);

    [SkippableFact]
    public void FixedStatement_ExtensionGetPinnableReference_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_pinnableReference);
}
