#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for a <c>[GpuReadOnly]</c> array that is cast to its array type and kept across a barrier: the
/// native CPU build compiles it, and the managed, native and CUDA paths read the same elements.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class ReadOnlyArrayCastDifferentialTests
{
    private const int Rows = 128;

    internal static readonly DifferentialCase s_castLocal = DiffCases.Create(
        ReadOnlyArrayCastDifferentialKernels.LaunchCastLocalKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("source", DiffValues.Int32(rng, rows), readOnly: true)
            .Out<int>("output", rows, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        inputName: "source",
        outputName: "output",
        // The barrier keeps the kernel from running as one sequential C# loop, so the oracle is the reference below
        reference: static (b, _) =>
        {
            var source = b.Get<int>("source");
            var output = b.Get<int>("output");
            for (var i = 0; i < source.Length; i++)
            {
                output[i] = source[i] + 1;
            }
        });

    [SkippableFact]
    public void CastLocal_ReadOnlyArrayHeldAcrossBarrier_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_castLocal);
}
