#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for property getters that change their struct, on variables and on readonly variables (#610).
/// </summary>
public class StructMutatingGetterDifferentialTests
{
    private const int Rows = 257;

    internal static readonly DifferentialCase s_mutatingGetter = DiffCases.Create(
        StructMutatingGetterDifferentialKernels.LaunchMutatingGetterKernel,
        inputs: static (rng, rows) =>
        {
            var inputs = DiffValues.Int32(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .Out<int>("r", rows, StructMutatingGetterDifferentialKernels.MutatingGetterColumns, OutputPolicy.Exact);
        },
        shapes: LaunchShapes.OneDimensionalMatrix(Rows),
        inputName: "inputs",
        outputName: "r");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void StructMutatingGetter_VariablesAndReadonlyVariables_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mutatingGetter);
}
