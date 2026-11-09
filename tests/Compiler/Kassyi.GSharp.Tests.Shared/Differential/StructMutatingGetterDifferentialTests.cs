#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for property getters that change their struct, on variables and on readonly variables (#610).
/// </summary>
public class StructMutatingGetterDifferentialTests
{
    private const int Rows = 257;

    private const string KernelName = nameof(StructMutatingGetterDifferentialKernels.MutatingGetterKernel);

    private static readonly DifferentialCase s_mutatingGetter = new(
        $"{nameof(StructMutatingGetterDifferentialKernels)}.{KernelName}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
        Inputs = static (rng, rows) =>
        {
            var inputs = DiffValues.Int32(rng, rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .Out<int>("r", rows, StructMutatingGetterDifferentialKernels.MutatingGetterColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => StructMutatingGetterDifferentialKernels.LaunchMutatingGetterKernel(
            b.Get<int>("inputs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => StructMutatingGetterDifferentialKernels.MutatingGetterKernel(
            b.Get<int>("inputs"),
            b.Get<int>("r")),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MutatingGetterOnVariablesAndReadonlyVariables_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mutatingGetter);
}
