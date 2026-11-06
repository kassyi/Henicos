#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <c>Gpu.ForUnroll</c> and <c>Gpu.GridStrideLoop</c> with reordered named arguments: the start,
/// count, bound, body and context are those the arguments name on every path, not those at their positions (#1260).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class MacroNamedArgumentDifferentialTests
{
    private const int Rows = 128;

    private static readonly DifferentialCase s_forUnroll = new(
        $"{nameof(MacroNamedArgumentDifferentialKernels)}.{nameof(MacroNamedArgumentDifferentialKernels.ForUnrollNamedArgumentsKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, MacroNamedArgumentDifferentialKernels.ForUnrollColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => MacroNamedArgumentDifferentialKernels.LaunchForUnrollNamedArgumentsKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => MacroNamedArgumentDifferentialKernels.ForUnrollNamedArgumentsKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_gridStrideLoop = new(
        $"{nameof(MacroNamedArgumentDifferentialKernels)}.{nameof(MacroNamedArgumentDifferentialKernels.GridStrideLoopNamedArgumentsKernel)}")
    {
        // Fewer threads than elements, so each thread runs several iterations
        Shape = LaunchShape.ForRows(Rows / 4, blockX: 32),
        Inputs = static (rng, _) =>
        {
            var (xs, _) = DiffValues.Int32Pairs(rng, Rows);
            return new DiffBuffers()
                .In("xs", xs)
                .InScalar("bias", rng.NextBelow(2000) - 1000)
                .Out<int>("r", Rows * 2, OutputPolicy.Exact);
        },
        Launch = static (b, s) => MacroNamedArgumentDifferentialKernels.LaunchGridStrideLoopNamedArgumentsKernel(
            b.Get<int>("xs"),
            b.Get<int>("r"),
            b.GetScalar<int>("bias"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            var r = b.Get<int>("r");
            int bias = b.GetScalar<int>("bias");
            for (int i = 0; i < xs.Length; i++)
            {
                r[i * 2] = (xs[i] & 0xFFFF) * 3 + 1;
                r[(i * 2) + 1] = (xs[i] & 0xFFFF) + bias;
            }
        },
    };

    // The oracle cannot run barriers; the reference is the loop with start 1, count 3 and start 2, count 2
    private static readonly DifferentialCase s_forUnrollWithBarrier = new(
        $"{nameof(MacroNamedArgumentDifferentialKernels)}.{nameof(MacroNamedArgumentDifferentialKernels.ForUnrollNamedArgumentsWithBarrierKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, _) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .Out<int>("r", rows, MacroNamedArgumentDifferentialKernels.BarrierColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => MacroNamedArgumentDifferentialKernels.LaunchForUnrollNamedArgumentsWithBarrierKernel(
            b.Get<int>("xs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            var r = b.Get<int>("r");
            for (int tid = 0; tid < xs.Length; tid++)
            {
                int x = xs[tid] & 0x3FF;
                int reordered = 0;
                for (int i = 1; i < 4; i++)
                {
                    reordered += (x + i) * (i + 2);
                }

                int acc = 1;
                for (int i = 2; i < 4; i++)
                {
                    acc = (acc * 3) + i + (x & 15);
                }

                r[tid * 2] = reordered;
                r[(tid * 2) + 1] = acc;
            }
        },
    };

    [SkippableFact]
    public void Test_ForUnrollNamedArguments_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_forUnroll);

    [SkippableFact]
    public void Test_GridStrideLoopNamedArguments_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_gridStrideLoop);

    [SkippableFact]
    public void Test_ForUnrollNamedArgumentsWithBarrier_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_forUnrollWithBarrier);
}
