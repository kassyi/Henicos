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

    internal static readonly DifferentialCase s_collectionInitializer = DiffCases.Create(
        CollectionInitializerDifferentialKernels.LaunchCollectionInitializerKernel,
        inputs: static (rng, rows) =>
        {
            var (xs, _) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("cells", DiffValues.Int32Pairs(rng, rows).Item1.Select(static v => v & 0xFFFF).ToArray())
                .Out<int>("r", rows, CollectionInitializerDifferentialKernels.Columns, OutputPolicy.Exact);
        },
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        firstInputName: "xs",
        secondInputName: "cells",
        outputName: "r");

    // C# cannot run a kernel that reaches a warp shuffle, so the reference computes the exchange with the lane
    // tid ^ 1
    internal static readonly DifferentialCase s_suspendingElements = DiffCases.Create(
        CollectionInitializerDifferentialKernels.LaunchCollectionInitializerSuspendingKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, CollectionInitializerDifferentialKernels.SuspendingColumns, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        inputName: "xs",
        outputName: "r",
        reference: static (b, _) =>
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
        });

    internal static readonly DifferentialCase s_acrossBarrier = DiffCases.Create(
        CollectionInitializerDifferentialKernels.LaunchCollectionInitializerAcrossBarrierKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", DiffValues.Int32(rng, rows))
            .In("cells", DiffValues.Int32(rng, rows).Select(static v => v & 0xFFFF).ToArray())
            .Out<int>("r", rows, CollectionInitializerDifferentialKernels.AcrossBarrierColumns, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        firstInputName: "xs",
        secondInputName: "cells",
        outputName: "r",
        // C# cannot run a kernel that reaches Gpu.SyncThreads()
        reference: static (b, _) =>
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
        });

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
