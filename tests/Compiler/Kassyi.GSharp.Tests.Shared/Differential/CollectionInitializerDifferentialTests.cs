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

    // C# cannot run a kernel that reaches a warp shuffle, so the reference computes the exchange with the lane
    // tid ^ 1
    internal static readonly DifferentialCase s_suspendingElements = new(
        nameof(CollectionInitializerDifferentialKernels) + "." +
        nameof(CollectionInitializerDifferentialKernels.CollectionInitializerSuspendingKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, CollectionInitializerDifferentialKernels.SuspendingColumns, OutputPolicy.Exact),
        Launch = static (b, s) => CollectionInitializerDifferentialKernels.LaunchCollectionInitializerSuspendingKernel(
            b.Get<int>("xs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            var r = b.Get<int>("r");
            var columns = CollectionInitializerDifferentialKernels.SuspendingColumns.Length;
            for (var tid = 0; tid < xs.Length; tid++)
            {
                var x = xs[tid] & 0xFF;
                var p = xs[tid ^ 1] & 0xFF;
                var o = tid * columns;
                r[o + 0] = p + 7;
                r[o + 1] = ((p & 15) * 16) + 7;
                r[o + 2] = p * x;
                r[o + 3] = p + 3;
                r[o + 4] = ((p & 15) * 16) + 3;
                r[o + 5] = p;
                r[o + 6] = p;
                r[o + 7] = 2 * p;
                r[o + 8] = p;
                r[o + 9] = 3 * p;
            }
        },
    };

    internal static readonly DifferentialCase s_acrossBarrier = new(
        nameof(CollectionInitializerDifferentialKernels) + "." +
        nameof(CollectionInitializerDifferentialKernels.CollectionInitializerAcrossBarrierKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", DiffValues.Int32(rng, rows))
            .In("cells", DiffValues.Int32(rng, rows).Select(static v => v & 0xFFFF).ToArray())
            .Out<int>("r", rows, CollectionInitializerDifferentialKernels.AcrossBarrierColumns, OutputPolicy.Exact),
        Launch = static (b, s) => CollectionInitializerDifferentialKernels.LaunchCollectionInitializerAcrossBarrierKernel(
            b.Get<int>("xs"),
            b.Get<int>("cells"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        // C# cannot run a kernel that reaches Gpu.SyncThreads()
        Reference = static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            var cells = b.Get<int>("cells");
            var r = b.Get<int>("r");
            var columns = CollectionInitializerDifferentialKernels.AcrossBarrierColumns.Length;
            for (var tid = 0; tid < xs.Length; tid++)
            {
                var x = xs[tid] & 0xFF;
                var cell = cells[tid];
                var o = tid * columns;
                r[o + 0] = x + x + 1;
                r[o + 1] = ((x & 15) * 16) + ((x + 1) & 15);
                r[o + 2] = x * 2;
                r[o + 3] = (x - 2) & 15;
                r[o + 4] = cell + cell + 100;
                cells[tid] = cell + 100;
            }
        },
    };

    [SkippableFact]
    public void CollectionInitializers_EveryForm_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_collectionInitializer);

    [SkippableFact]
    public void SuspendingElements_FirstValue_MatchTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_suspendingElements);

    [SkippableFact]
    public void InitializersAcrossBarrier_LiveAcrossSyncThreads_MatchTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_acrossBarrier);
}
