#nullable enable
using System;
using System.Runtime.CompilerServices;
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
        UsingIteratorExitDifferentialKernels.LaunchUsingStatementReturnFromIterator);

    internal static readonly DifferentialCase s_usingDeclarationReturn = CreateTwoColumnCase(
        UsingIteratorExitDifferentialKernels.LaunchUsingDeclarationReturnFromIterator);

    internal static readonly DifferentialCase s_deviceFunctionReturn = CreateTwoColumnCase(
        UsingIteratorExitDifferentialKernels.LaunchDeviceFunctionReturnFromIterator);

    internal static readonly DifferentialCase s_breakContinueAndGotoOutsideUsing = CreateTwoColumnCase(
        UsingIteratorExitDifferentialKernels.LaunchBreakContinueAndGotoOutsideUsingFromIterator);

    internal static readonly DifferentialCase s_nestedUsingsReturn = CreateTwoColumnCase(
        UsingIteratorExitDifferentialKernels.LaunchNestedUsingsReturnFromIterator);

    internal static readonly DifferentialCase s_normalCompletionWithoutReturn = CreateTwoColumnCase(
        UsingIteratorExitDifferentialKernels.LaunchNormalCompletionWithoutReturnFromIterator);

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

    private static DifferentialCase CreateTwoColumnCase(
        Int32PairLaunch launch,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: static (rng, rows) => new DiffBuffers()
                .In("xs", CreateInputs(rng, rows))
                .Out<int>("r", rows, ["dispose_log", "acc"], OutputPolicy.Exact),
            shape: s_shape,
            inputName: "xs",
            outputName: "r",
            launchExpression: launchExpression);

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
