#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for structs that only the bodies of another struct create (#1158).
/// </summary>
public class StructBodyDependencyDifferentialTests
{
    private const int Rows = 257;

    private const string KernelName = nameof(StructBodyDependencyDifferentialKernels.StructCreatedOnlyInBodiesKernel);

    private static readonly DifferentialCase s_createdOnlyInBodies = new(
        $"{nameof(StructBodyDependencyDifferentialKernels)}.{KernelName}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
        Inputs = static (rng, rows) =>
        {
            var inputs = DiffValues.Int32(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .Out<int>(
                    "r",
                    rows,
                    StructBodyDependencyDifferentialKernels.CreatedOnlyInBodiesColumns,
                    OutputPolicy.Exact);
        },
        Launch = static (b, s) => StructBodyDependencyDifferentialKernels.LaunchStructCreatedOnlyInBodiesKernel(
            b.Get<int>("inputs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => StructBodyDependencyDifferentialKernels.StructCreatedOnlyInBodiesKernel(
            b.Get<int>("inputs"),
            b.Get<int>("r")),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StructCreatedOnlyInTheBodiesOfAnotherStruct_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_createdOnlyInBodies);
}
