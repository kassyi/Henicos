#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for the integer shapes of <see cref="NativeOptimizerDifferentialKernels"/>, which the MSVC global
/// optimizer miscompiles on the native path (#1078). Every row starts with the value that triggers the miscompile
/// (<see cref="int.MinValue"/>, a negative <c>short</c>, <see cref="short.MaxValue"/>), so each seed reaches it.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class NativeOptimizerDifferentialTests
{
    private const int Rows = 64;

    private static readonly int[] s_triggers =
    [
        int.MinValue,
        int.MinValue + 1,
        -1,
        short.MaxValue,
        short.MinValue,
        unchecked((int)0xFFFF_8000),
        0x0001_7FFF,
        int.MaxValue,
        0,
        1,
    ];

    private static int[] Values(DiffRandom rng, int rows)
    {
        var values = DiffValues.Int32(rng, rows);
        for (int k = 0; k < s_triggers.Length && k < rows; k++)
        {
            values[k] = s_triggers[k];
        }

        return values;
    }

    private static readonly DifferentialCase s_negateThenDivide = new(
        DiffKernelNames.Of(
            typeof(NativeOptimizerDifferentialKernels),
            nameof(NativeOptimizerDifferentialKernels.NegateThenDivideKernel)))
    {
        Shape = LaunchShapes.Linear(Rows, 32),
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("src", Values(rng, shape.Rows), readOnly: true)
            .Out<ulong>(
                "dst",
                shape.Rows,
                NativeOptimizerDifferentialKernels.NegateThenDivideColumns,
                OutputPolicy.Exact),
        Launch = static (b, s) => NativeOptimizerDifferentialKernels.LaunchNegateThenDivideKernel(
            b.Get<int>("src"),
            b.Get<ulong>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => NativeOptimizerDifferentialKernels.NegateThenDivideKernel(
            b.Get<int>("src"),
            b.Get<ulong>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_shortWidenedTwice = new(
        DiffKernelNames.Of(
            typeof(NativeOptimizerDifferentialKernels),
            nameof(NativeOptimizerDifferentialKernels.ShortWidenedTwiceKernel)))
    {
        Shape = LaunchShapes.Linear(Rows, 32),
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("src", Values(rng, shape.Rows), readOnly: true)
            .Out<ulong>(
                "dst",
                shape.Rows,
                NativeOptimizerDifferentialKernels.ShortWidenedTwiceColumns,
                OutputPolicy.Exact),
        Launch = static (b, s) => NativeOptimizerDifferentialKernels.LaunchShortWidenedTwiceKernel(
            b.Get<int>("src"),
            b.Get<ulong>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => NativeOptimizerDifferentialKernels.ShortWidenedTwiceKernel(
            b.Get<int>("src"),
            b.Get<ulong>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_shortIncrementPicksRefTarget = new(
        DiffKernelNames.Of(
            typeof(NativeOptimizerDifferentialKernels),
            nameof(NativeOptimizerDifferentialKernels.ShortIncrementPicksRefTargetKernel)))
    {
        Shape = LaunchShapes.Linear(Rows, 32),
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("src", Values(rng, shape.Rows), readOnly: true)
            .Out<int>(
                "dst",
                shape.Rows,
                NativeOptimizerDifferentialKernels.ShortIncrementPicksRefTargetColumns,
                OutputPolicy.Exact),
        Launch = static (b, s) => NativeOptimizerDifferentialKernels.LaunchShortIncrementPicksRefTargetKernel(
            b.Get<int>("src"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => NativeOptimizerDifferentialKernels.ShortIncrementPicksRefTargetKernel(
            b.Get<int>("src"),
            b.Get<int>("dst"),
            s.Rows),
    };

    [SkippableFact]
    public void NegateThenDivide_MinValueDividend_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_negateThenDivide);

    [SkippableFact]
    public void ShortWidenedTwice_NegativeShort_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_shortWidenedTwice);

    [SkippableFact]
    public void ShortIncrementPicksRefTarget_MaxValueWraps_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_shortIncrementPicksRefTarget);
}
