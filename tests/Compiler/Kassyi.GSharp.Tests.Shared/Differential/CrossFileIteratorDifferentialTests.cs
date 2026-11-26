#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential execution tests for cross-file iterators enumerated through local variables
/// (<see cref="CrossFileIteratorLibrary"/> and <see cref="CrossFileIteratorKernels"/>).
/// Compares managed and native CPU paths against the sequential C# oracle across seeds.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.HappyPath)]
public class CrossFileIteratorDifferentialTests
{
    private const int Rows = 64;

    private static readonly int[] s_boundaryInputs =
    [
        0, 1, -1, 2, -2, 10, -10, 100, -100, 1000, -1000,
        int.MaxValue / 16, int.MinValue / 16, 7, -7, 42
    ];

    internal static readonly DifferentialCase s_callArgCase = DiffCases.Create(
        CrossFileIteratorKernels.LaunchCrossFileIteratorCallArgKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("input", DiffValues.Mixed(rng, rows, s_boundaryInputs, DiffValues.NextInt32))
            .Out<int>("output", rows, CrossFileIteratorKernels.CallArgColumns, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        inputName: "input",
        outputName: "output");

    internal static readonly DifferentialCase s_twiceCase = DiffCases.Create(
        CrossFileIteratorKernels.LaunchCrossFileIteratorTwiceKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("input", DiffValues.Mixed(rng, rows, s_boundaryInputs, DiffValues.NextInt32))
            .Out<int>("output", rows, CrossFileIteratorKernels.TwiceColumns, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        inputName: "input",
        outputName: "output");

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void CrossFileIterator_CallArg_MatchesOracleOnEveryPath() =>
        DifferentialRunner.Run(s_callArgCase);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void CrossFileIterator_Twice_MatchesOracleOnEveryPath() =>
        DifferentialRunner.Run(s_twiceCase);
}
