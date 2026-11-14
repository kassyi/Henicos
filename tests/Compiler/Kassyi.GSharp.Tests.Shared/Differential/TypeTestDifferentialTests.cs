#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential test for <c>is</c> on a value: C# decides the test by the type of the value (an identity, a nullable of
/// it, a type it boxes to), and still evaluates the operand (#642).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.HappyPath)]
public class TypeTestDifferentialTests
{
    private const int Rows = 128;

    internal static readonly DifferentialCase s_typeTest = new(
        nameof(TypeTestDifferentialKernels) + "." + nameof(TypeTestDifferentialKernels.TypeTestKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, TypeTestDifferentialKernels.Columns, OutputPolicy.Exact),
        Launch = static (b, s) => TypeTestDifferentialKernels.LaunchTypeTestKernel(
            b.Get<int>("inputs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    public void TypeTest_ValueOperand_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_typeTest);
}
