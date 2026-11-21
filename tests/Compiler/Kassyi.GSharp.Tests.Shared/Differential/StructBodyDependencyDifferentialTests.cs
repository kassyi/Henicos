#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for structs that only the bodies of another struct create (#1158).
/// </summary>
public class StructBodyDependencyDifferentialTests
{
    private const int Rows = 257;

    internal static readonly DifferentialCase s_createdOnlyInBodies = DiffCases.Create(
        StructBodyDependencyDifferentialKernels.LaunchStructCreatedOnlyInBodiesKernel,
        inputs: static (rng, rows) =>
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
        shapes: LaunchShapes.OneDimensionalMatrix(Rows),
        inputName: "inputs",
        outputName: "r");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void StructDefinition_CreatedOnlyInBodiesOfAnotherStruct_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_createdOnlyInBodies);
}
