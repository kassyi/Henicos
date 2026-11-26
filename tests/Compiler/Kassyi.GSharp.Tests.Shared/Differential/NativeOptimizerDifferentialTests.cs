#nullable enable
using System;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for the integer shapes of <see cref="NativeOptimizerDifferentialKernels"/>, which the MSVC global
/// optimizer miscompiles on the native path (#1078). The first rows of every seed hold the values that trigger the
/// miscompiles (<see cref="int.MinValue"/>, a negative <c>short</c>, <see cref="short.MaxValue"/>, an <c>sbyte</c> that
/// wraps below <see cref="sbyte.MinValue"/>), so each seed reaches them; the other rows are random.
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
        sbyte.MinValue,
        -2,
        -100,
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

    // One case: the kernel reads src[rows] and writes columns.Length values of T per row to dst.
    private static DifferentialCase CreateCase<T>(
        DiffLaunch<int[], T[], int> launch,
        string[] columns,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "")
        where T : unmanaged =>
        DiffCases.CreateRows(
            launch,
            inputs: (rng, shape) => new DiffBuffers()
                .In("src", Values(rng, shape.Rows), readOnly: true)
                .Out<T>("dst", shape.Rows, columns, OutputPolicy.Exact),
            shape: LaunchShapes.Linear(Rows, 32),
            inputName: "src",
            outputName: "dst",
            launchExpression: launchExpression);

    internal static readonly DifferentialCase s_negateThenDivide = CreateCase<ulong>(
        NativeOptimizerDifferentialKernels.LaunchNegateThenDivideKernel,
        NativeOptimizerDifferentialKernels.NegateThenDivideColumns);

    internal static readonly DifferentialCase s_shortWidenedTwice = CreateCase<ulong>(
        NativeOptimizerDifferentialKernels.LaunchShortWidenedTwiceKernel,
        NativeOptimizerDifferentialKernels.ShortWidenedTwiceColumns);

    internal static readonly DifferentialCase s_shortIncrementPicksRefTarget = CreateCase<int>(
        NativeOptimizerDifferentialKernels.LaunchShortIncrementPicksRefTargetKernel,
        NativeOptimizerDifferentialKernels.ShortIncrementPicksRefTargetColumns);

    internal static readonly DifferentialCase s_sbyteSubtractPicksRefTarget = CreateCase<int>(
        NativeOptimizerDifferentialKernels.LaunchSByteSubtractPicksRefTargetKernel,
        NativeOptimizerDifferentialKernels.SByteSubtractPicksRefTargetColumns);

    [SkippableFact]
    public void NegateThenDivide_MinValueDividend_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_negateThenDivide);

    [SkippableFact]
    public void ShortWidenedTwice_NegativeShort_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_shortWidenedTwice);

    [SkippableFact]
    public void ShortIncrementPicksRefTarget_MaxValueWraps_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_shortIncrementPicksRefTarget);

    [SkippableFact]
    public void SByteSubtractPicksRefTarget_SubtractionWraps_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_sbyteSubtractPicksRefTarget);
}
