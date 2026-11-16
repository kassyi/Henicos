#nullable enable
using System;
using System.Threading.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for C# 15 labeled <c>break</c> and <c>continue</c> statements (#1990):
/// Managed C#, Native CPU C++ and CUDA C++ against the sequential C# oracle.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class LabeledJumpDifferentialTests
{
    private const int Rows = 64;
    private static readonly LaunchShape s_shape = LaunchShape.ForRows(Rows, blockX: 32);

    internal static readonly DifferentialCase s_breakAndContinueOuterFor = CreateSingleColumn(
        nameof(LabeledJumpDifferentialKernels.BreakAndContinueOuterFor),
        static (xs, r, s) => LabeledJumpDifferentialKernels.LaunchBreakAndContinueOuterFor(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_labeledWhile = CreateSingleColumn(
        nameof(LabeledJumpDifferentialKernels.LabeledWhile),
        static (xs, r, s) => LabeledJumpDifferentialKernels.LaunchLabeledWhile(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_switchInLabeledLoop = CreateSingleColumn(
        nameof(LabeledJumpDifferentialKernels.SwitchInLabeledLoop),
        static (xs, r, s) => LabeledJumpDifferentialKernels.LaunchSwitchInLabeledLoop(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_continueOuterRunsTheIncrement = CreateSingleColumn(
        nameof(LabeledJumpDifferentialKernels.ContinueOuterRunsTheIncrement),
        static (xs, r, s) => LabeledJumpDifferentialKernels.LaunchContinueOuterRunsTheIncrement(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_continueOuterEvaluatesTheDoCondition = CreateSingleColumn(
        nameof(LabeledJumpDifferentialKernels.ContinueOuterEvaluatesTheDoCondition),
        static (xs, r, s) => LabeledJumpDifferentialKernels.LaunchContinueOuterEvaluatesTheDoCondition(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_middleOfThreeLoops = CreateSingleColumn(
        nameof(LabeledJumpDifferentialKernels.MiddleOfThreeLoops),
        static (xs, r, s) => LabeledJumpDifferentialKernels.LaunchMiddleOfThreeLoops(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_twoLoopsWithTheSameLabel = CreateSingleColumn(
        nameof(LabeledJumpDifferentialKernels.TwoLoopsWithTheSameLabel),
        static (xs, r, s) => LabeledJumpDifferentialKernels.LaunchTwoLoopsWithTheSameLabel(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_forEachOverAnArray = CreateSingleColumn(
        nameof(LabeledJumpDifferentialKernels.ForEachOverAnArray),
        static (xs, r, s) => LabeledJumpDifferentialKernels.LaunchForEachOverAnArray(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_forEachOverAnIterator = CreateSingleColumn(
        nameof(LabeledJumpDifferentialKernels.ForEachOverAnIterator),
        static (xs, r, s) => LabeledJumpDifferentialKernels.LaunchForEachOverAnIterator(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_breakLabeledSwitchFromALoop = CreateSingleColumn(
        nameof(LabeledJumpDifferentialKernels.BreakLabeledSwitchFromALoop),
        static (xs, r, s) => LabeledJumpDifferentialKernels.LaunchBreakLabeledSwitchFromALoop(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_usingInsideTheLabeledLoop = new(
        $"{nameof(LabeledJumpDifferentialKernels)}.{nameof(LabeledJumpDifferentialKernels.UsingInsideTheLabeledLoop)}")
    {
        Shape = s_shape,
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", CreateInputs(rng, rows))
            .Out<int>("r", rows, ["dispose_log", "acc"], OutputPolicy.Exact),
        Launch = static (b, s) => LabeledJumpDifferentialKernels.LaunchUsingInsideTheLabeledLoop(
            b.Get<int>("xs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_labeledLoopInAKernelWithoutABarrier = CreateSingleColumn(
        nameof(LabeledJumpDifferentialKernels.LabeledLoopInAKernelWithoutABarrier),
        static (xs, r, s) => LabeledJumpDifferentialKernels.LaunchLabeledLoopInAKernelWithoutABarrier(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_unlabeledJumpsNextToLabeledOnes = CreateSingleColumn(
        nameof(LabeledJumpDifferentialKernels.UnlabeledJumpsNextToLabeledOnes),
        static (xs, r, s) => LabeledJumpDifferentialKernels.LaunchUnlabeledJumpsNextToLabeledOnes(
            xs, r, gridDimX: s.GridX, blockDimX: s.BlockX));

    [SkippableFact]
    public void BreakAndContinueOuterFor_MatchesOracle() =>
        DifferentialRunner.Run(s_breakAndContinueOuterFor);

    [SkippableFact(Timeout = 5000)]
    public async Task LabeledWhile_MatchesOracle() =>
        await Task.Run(() => DifferentialRunner.Run(s_labeledWhile));

    [SkippableFact]
    public void SwitchInLabeledLoop_MatchesOracle() =>
        DifferentialRunner.Run(s_switchInLabeledLoop);

    [SkippableFact]
    public void ContinueOuterRunsTheIncrement_MatchesOracle() =>
        DifferentialRunner.Run(s_continueOuterRunsTheIncrement);

    [SkippableFact]
    public void ContinueOuterEvaluatesTheDoCondition_MatchesOracle() =>
        DifferentialRunner.Run(s_continueOuterEvaluatesTheDoCondition);

    [SkippableFact]
    public void MiddleOfThreeLoops_MatchesOracle() =>
        DifferentialRunner.Run(s_middleOfThreeLoops);

    [SkippableFact]
    public void TwoLoopsWithTheSameLabel_MatchesOracle() =>
        DifferentialRunner.Run(s_twoLoopsWithTheSameLabel);

    [SkippableFact]
    public void ForEachOverAnArray_MatchesOracle() =>
        DifferentialRunner.Run(s_forEachOverAnArray);

    [SkippableFact]
    public void ForEachOverAnIterator_MatchesOracle() =>
        DifferentialRunner.Run(s_forEachOverAnIterator);

    [SkippableFact]
    public void BreakLabeledSwitchFromALoop_MatchesOracle() =>
        DifferentialRunner.Run(s_breakLabeledSwitchFromALoop);

    [SkippableFact]
    public void UsingInsideTheLabeledLoop_MatchesOracle() =>
        DifferentialRunner.Run(s_usingInsideTheLabeledLoop);

    [SkippableFact]
    public void LabeledLoopInAKernelWithoutABarrier_MatchesOracle() =>
        DifferentialRunner.Run(s_labeledLoopInAKernelWithoutABarrier);

    [SkippableFact]
    public void UnlabeledJumpsNextToLabeledOnes_MatchesOracle() =>
        DifferentialRunner.Run(s_unlabeledJumpsNextToLabeledOnes);

    private static DifferentialCase CreateSingleColumn(string kernelName, Action<int[], int[], LaunchShape> launch) =>
        new($"{nameof(LabeledJumpDifferentialKernels)}.{kernelName}")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("xs", CreateInputs(rng, rows))
                .Out<int>("r", rows, OutputPolicy.Exact),
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
