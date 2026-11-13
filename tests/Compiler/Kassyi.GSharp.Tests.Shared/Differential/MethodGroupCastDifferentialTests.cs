#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential test for a device function named through a cast to its delegate type: the CUDA output has no type to
/// cast to for a delegate, which is a lambda of its own type (#1725).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.HappyPath)]
public class MethodGroupCastDifferentialTests
{
    private const int Rows = 128;

    internal static readonly DifferentialCase s_methodGroupCast = new(
        nameof(MethodGroupCastDifferentialKernels) + "." +
        nameof(MethodGroupCastDifferentialKernels.MethodGroupCastKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, OutputPolicy.Exact),
        Launch = static (b, s) => MethodGroupCastDifferentialKernels.LaunchMethodGroupCastKernel(
            b.Get<int>("inputs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    public void MethodGroupCast_DeviceFunction_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_methodGroupCast);
}
