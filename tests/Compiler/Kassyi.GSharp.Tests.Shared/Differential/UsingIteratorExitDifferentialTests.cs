#nullable enable
using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for jumps leaving a <c>using</c> statement or declaration from inside an inlined iterator
/// <c>foreach</c> body: Managed C#, Native CPU C++ and CUDA C++ against the sequential C# oracle.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class UsingIteratorExitDifferentialTests
{
    private const int Rows = 64;
    private static readonly LaunchShape s_shape = LaunchShape.ForRows(Rows, blockX: 32);

    internal static readonly DifferentialCase s_usingStatementReturn = CreateTwoColumnCase(
        nameof(UsingIteratorExitDifferentialKernels.UsingStatementReturnFromIterator),
        static (xs, r, s) => UsingIteratorExitDifferentialKernels.LaunchUsingStatementReturnFromIterator(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_usingDeclarationReturn = CreateTwoColumnCase(
        nameof(UsingIteratorExitDifferentialKernels.UsingDeclarationReturnFromIterator),
        static (xs, r, s) => UsingIteratorExitDifferentialKernels.LaunchUsingDeclarationReturnFromIterator(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_deviceFunctionReturn = CreateTwoColumnCase(
        nameof(UsingIteratorExitDifferentialKernels.DeviceFunctionReturnFromIterator),
        static (xs, r, s) => UsingIteratorExitDifferentialKernels.LaunchDeviceFunctionReturnFromIterator(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_breakContinueAndGotoOutsideUsing = CreateTwoColumnCase(
        nameof(UsingIteratorExitDifferentialKernels.BreakContinueAndGotoOutsideUsingFromIterator),
        static (xs, r, s) => UsingIteratorExitDifferentialKernels.LaunchBreakContinueAndGotoOutsideUsingFromIterator(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_nestedUsingsReturn = CreateTwoColumnCase(
        nameof(UsingIteratorExitDifferentialKernels.NestedUsingsReturnFromIterator),
        static (xs, r, s) => UsingIteratorExitDifferentialKernels.LaunchNestedUsingsReturnFromIterator(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_normalCompletionWithoutReturn = CreateTwoColumnCase(
        nameof(UsingIteratorExitDifferentialKernels.NormalCompletionWithoutReturnFromIterator),
        static (xs, r, s) => UsingIteratorExitDifferentialKernels.LaunchNormalCompletionWithoutReturnFromIterator(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    [SkippableFact]
    public void UsingStatementReturnFromIterator_MatchesOracle() =>
        DifferentialRunner.Run(s_usingStatementReturn);

    [SkippableFact]
    public void UsingDeclarationReturnFromIterator_MatchesOracle() =>
        DifferentialRunner.Run(s_usingDeclarationReturn);

    [SkippableFact]
    public void DeviceFunctionReturnFromIterator_MatchesOracle() =>
        DifferentialRunner.Run(s_deviceFunctionReturn);

    [SkippableFact]
    public void BreakContinueAndGotoOutsideUsingFromIterator_MatchesOracle() =>
        DifferentialRunner.Run(s_breakContinueAndGotoOutsideUsing);

    [SkippableFact]
    public void NestedUsingsReturnFromIterator_MatchesOracle() =>
        DifferentialRunner.Run(s_nestedUsingsReturn);

    [SkippableFact]
    public void NormalCompletionWithoutReturnFromIterator_MatchesOracle() =>
        DifferentialRunner.Run(s_normalCompletionWithoutReturn);

    private static DifferentialCase CreateTwoColumnCase(string kernelName, Action<int[], int[], LaunchShape> launch) =>
        new($"{nameof(UsingIteratorExitDifferentialKernels)}.{kernelName}")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("xs", CreateInputs(rng, rows))
                .Out<int>("r", rows, ["dispose_log", "acc"], OutputPolicy.Exact),
            Launch = (b, s) => launch(b.Get<int>("xs"), b.Get<int>("r"), s),
            OracleIsLaunch = true,
        };

    private static int[] CreateInputs(DiffRandom rng, int rows)
    {
        var xs = DiffValues.Int32(rng, rows);
        for (int i = 0; i < Math.Min(16, rows); i++)
        {
            xs[i] = i;
        }

        return xs;
    }
}
