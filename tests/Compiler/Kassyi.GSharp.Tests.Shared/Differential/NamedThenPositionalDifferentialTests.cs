#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for a named argument in its own position followed by a positional argument: every argument reaches
/// the parameter C# binds it to on every path, and an omitted <c>[Optional]</c> parameter receives <c>default</c> (#1263).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class NamedThenPositionalDifferentialTests
{
    private const int Rows = 128;

    private static readonly DifferentialCase s_namedThenPositional = new(
        $"{nameof(NamedThenPositionalDifferentialKernels)}.{nameof(NamedThenPositionalDifferentialKernels.NamedThenPositionalKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, NamedThenPositionalDifferentialKernels.Columns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => NamedThenPositionalDifferentialKernels.LaunchNamedThenPositionalKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => NamedThenPositionalDifferentialKernels.NamedThenPositionalKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r")),
    };

    // The oracle cannot run barriers; the reference is SubAcrossBarrier(x, y)
    private static readonly DifferentialCase s_suspendingCall = new(
        $"{nameof(NamedThenPositionalDifferentialKernels)}.{nameof(NamedThenPositionalDifferentialKernels.NamedThenPositionalSuspendingCallKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, OutputPolicy.Exact);
        },
        Launch = static (b, s) => NamedThenPositionalDifferentialKernels.LaunchNamedThenPositionalSuspendingCallKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            var ys = b.Get<int>("ys");
            var r = b.Get<int>("r");
            for (int tid = 0; tid < xs.Length; tid++)
            {
                r[tid] = (xs[tid] & 0x3FF) - (ys[tid] & 0x3FF);
            }
        },
    };

    [SkippableFact]
    public void Test_NamedThenPositional_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_namedThenPositional);

    [SkippableFact]
    public void Test_NamedThenPositionalSuspendingCall_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_suspendingCall);
}
