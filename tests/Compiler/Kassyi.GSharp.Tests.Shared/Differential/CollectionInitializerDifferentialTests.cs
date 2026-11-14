#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for collection initializers in GPU code: every element reaches the <c>Add</c> that C# binds, in
/// the written order, on every path (#1899).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.HappyPath)]
public class CollectionInitializerDifferentialTests
{
    private const int Rows = 128;

    internal static readonly DifferentialCase s_collectionInitializer = new(
        nameof(CollectionInitializerDifferentialKernels) + "." +
        nameof(CollectionInitializerDifferentialKernels.CollectionInitializerKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, _) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("cells", DiffValues.Int32Pairs(rng, rows).Item1.Select(static v => v & 0xFFFF).ToArray())
                .Out<int>("r", rows, CollectionInitializerDifferentialKernels.Columns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => CollectionInitializerDifferentialKernels.LaunchCollectionInitializerKernel(
            b.Get<int>("xs"),
            b.Get<int>("cells"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    public void CollectionInitializers_EveryForm_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_collectionInitializer);
}
