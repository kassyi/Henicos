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

    internal static readonly DifferentialCase s_methodGroupCast = DiffCases.Create(
        MethodGroupCastDifferentialKernels.LaunchMethodGroupCastKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        inputName: "inputs",
        outputName: "r");

    internal static readonly DifferentialCase s_userDelegate = DiffCases.Create(
        MethodGroupCastDifferentialKernels.LaunchUserDelegateKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        inputName: "inputs",
        outputName: "r");

    internal static readonly DifferentialCase s_byRefDelegate = DiffCases.Create(
        MethodGroupCastDifferentialKernels.LaunchByRefDelegateKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        inputName: "inputs",
        outputName: "r");

    [SkippableFact]
    public void MethodGroupCast_DeviceFunction_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_methodGroupCast);

    [SkippableFact]
    public void UserDelegate_DeclaredType_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_userDelegate);

    [SkippableFact]
    public void ByRefDelegate_RefOutInParametersAndNull_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_byRefDelegate);
}
