#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for prefix and postfix <c>++</c> and <c>--</c> on narrow integer types
/// (<see cref="sbyte"/>, <see cref="byte"/>, <see cref="short"/>, <see cref="ushort"/>, <see cref="char"/>) (#1079).
/// </summary>
public class NarrowStepDifferentialTests
{
    private static readonly int[] s_narrowStepBoundaries =
    [
        0,
        1,
        -1,
        sbyte.MinValue,
        sbyte.MinValue + 1,
        sbyte.MaxValue - 1,
        sbyte.MaxValue,
        byte.MaxValue - 1,
        byte.MaxValue,
        short.MinValue,
        short.MinValue + 1,
        short.MaxValue - 1,
        short.MaxValue,
        ushort.MaxValue - 1,
        ushort.MaxValue,
    ];

    private static readonly DifferentialCase s_narrowStep = new($"{nameof(NarrowStepDifferentialKernels)}.{nameof(NarrowStepDifferentialKernels.NarrowStepMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(32, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Mixed(rng, rows, s_narrowStepBoundaries, static r => unchecked((int)r.NextUInt64())))
            .Out<long>("r", rows, NarrowStepDifferentialKernels.NarrowStepColumns, OutputPolicy.Exact),
        Launch = static (b, s) => NarrowStepDifferentialKernels.LaunchNarrowStepMatrixKernel(
            b.Get<int>("a"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => NarrowStepDifferentialKernels.NarrowStepMatrixKernel(
            b.Get<int>("a"),
            b.Get<long>("r")
        ),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NarrowStep_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_narrowStep);
}
