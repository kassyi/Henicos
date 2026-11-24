#nullable enable
using System;
using System.Runtime.CompilerServices;
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
        LabeledJumpDifferentialKernels.LaunchBreakAndContinueOuterFor);

    internal static readonly DifferentialCase s_labeledWhile = CreateSingleColumn(
        LabeledJumpDifferentialKernels.LaunchLabeledWhile);

    internal static readonly DifferentialCase s_switchInLabeledLoop = CreateSingleColumn(
        LabeledJumpDifferentialKernels.LaunchSwitchInLabeledLoop);

    internal static readonly DifferentialCase s_continueOuterRunsTheIncrement = CreateSingleColumn(
        LabeledJumpDifferentialKernels.LaunchContinueOuterRunsTheIncrement);

    internal static readonly DifferentialCase s_continueOuterEvaluatesTheDoCondition = CreateSingleColumn(
        LabeledJumpDifferentialKernels.LaunchContinueOuterEvaluatesTheDoCondition);

    internal static readonly DifferentialCase s_middleOfThreeLoops = CreateSingleColumn(
        LabeledJumpDifferentialKernels.LaunchMiddleOfThreeLoops);

    internal static readonly DifferentialCase s_twoLoopsWithTheSameLabel = CreateSingleColumn(
        LabeledJumpDifferentialKernels.LaunchTwoLoopsWithTheSameLabel);

    internal static readonly DifferentialCase s_forEachOverAnArray = CreateSingleColumn(
        LabeledJumpDifferentialKernels.LaunchForEachOverAnArray);

    internal static readonly DifferentialCase s_forEachOverAnIterator = CreateSingleColumn(
        LabeledJumpDifferentialKernels.LaunchForEachOverAnIterator);

    internal static readonly DifferentialCase s_breakLabeledSwitchFromALoop = CreateSingleColumn(
        LabeledJumpDifferentialKernels.LaunchBreakLabeledSwitchFromALoop);

    internal static readonly DifferentialCase s_usingInsideTheLabeledLoop = DiffCases.Create(
        LabeledJumpDifferentialKernels.LaunchUsingInsideTheLabeledLoop,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", CreateInputs(rng, rows))
            .Out<int>("r", rows, ["dispose_log", "acc"], OutputPolicy.Exact),
        shape: s_shape,
        inputName: "xs",
        outputName: "r");

    internal static readonly DifferentialCase s_labeledLoopInAKernelWithoutABarrier = CreateSingleColumn(
        LabeledJumpDifferentialKernels.LaunchLabeledLoopInAKernelWithoutABarrier);

    internal static readonly DifferentialCase s_unlabeledJumpsNextToLabeledOnes = CreateSingleColumn(
        LabeledJumpDifferentialKernels.LaunchUnlabeledJumpsNextToLabeledOnes);

    internal static readonly DifferentialCase s_unrolledLabeledLoops = CreateSingleColumn(
        LabeledJumpDifferentialKernels.LaunchUnrolledLabeledLoops);

    [SkippableFact]
    public void BreakAndContinueOuterFor_MatchesOracle() =>
        DifferentialRunner.Run(s_breakAndContinueOuterFor);

    // No timeout: both loops of the kernel are bounded (i grows first in the outer body, the inner one stops at
    // safety), so a wrong jump gives a wrong value, which the comparison catches. A wall-clock limit in a parallel run
    // measured the load of the runner instead (#2043).
    [SkippableFact]
    public void LabeledWhile_MatchesOracle() =>
        DifferentialRunner.Run(s_labeledWhile);

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

    [SkippableFact]
    public void UnrolledLabeledLoops_MatchesOracle() =>
        DifferentialRunner.Run(s_unrolledLabeledLoops);

    // The kernels with a split point in the labeled loop or switch (a barrier, a Gpu.ForUnroll body that holds one, a
    // Gpu.SyncGrid body, a call that suspends) are compared with a reference of the same code without the split point,
    // which the sequential C# launch cannot run. u (the first input) picks the jumps that are taken.
    [SkippableTheory(Timeout = 60000)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task BarrierLabeledKernel_MatchesReference(int uValue) =>
        await RunBarrierCase(
            LabeledJumpDifferentialKernels.LaunchBarrierLabeledKernel,
            uValue,
            static (tid, _, _) =>
            {
                int s = 0;

                outer:
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        if (j == 1 && i == 2) break outer;
                        if (j == 2) continue outer;
                        s += (i * 4) + j;
                    }

                    s += 100;
                }

                return s + (tid & 31);
            });

    [SkippableTheory(Timeout = 60000)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task BarrierInTheOuterBodyOnly_MatchesReference(int uValue) =>
        await RunBarrierCase(
            LabeledJumpDifferentialKernels.LaunchBarrierInTheOuterBodyOnly,
            uValue,
            static (_, x, u) =>
            {
                int s = 0;

                outer:
                for (int i = 0; i < 4; i++, s++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        if (j == 1 && ((i + u) & 1) == 0)
                        {
                            s += 10 + (x & 3);
                            continue outer;
                        }

                        if (i == 2 && j == 2)
                        {
                            s += 1000;
                            break outer;
                        }

                        s += (x & 3) + j;
                    }

                    s += 100;
                }

                return s;
            });

    [SkippableTheory(Timeout = 60000)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task BarrierOutsideTheLabeledLoop_MatchesReference(int uValue) =>
        await RunBarrierCase(
            LabeledJumpDifferentialKernels.LaunchBarrierOutsideTheLabeledLoop,
            uValue,
            static (_, x, u) =>
            {
                int s = 0;
                for (int i = 0; i < 3; i++)
                {
                    inner:
                    for (int j = 0; j < 4; j++)
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            if (k == 1 && ((j + u) & 1) == 0)
                            {
                                s += 5 + (x & 3);
                                continue inner;
                            }

                            if (j == 2 && k == 2)
                            {
                                s += 70;
                                break inner;
                            }

                            s += j + k + i;
                        }

                        s += 7;
                    }

                    s += 100;
                }

                return s;
            });

    [SkippableTheory(Timeout = 60000)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SwitchWithABarrierInALabeledLoop_MatchesReference(int uValue) =>
        await RunBarrierCase(
            LabeledJumpDifferentialKernels.LaunchSwitchWithABarrierInALabeledLoop,
            uValue,
            static (_, x, u) =>
            {
                int s = 0;

                outer:
                for (int i = 0; i < 5; i++)
                {
                    sw:
                    switch ((i + u) & 3)
                    {
                        case 0:
                            s += 1 + (x & 1);
                            continue outer;
                        case 1:
                            if (i >= 2) break outer;
                            s += 2;
                            break;
                        case 2:
                            for (int j = 0; j < 3; j++)
                            {
                                if (j == 1) break sw;
                                if ((i & 1) == 1) continue outer;
                                s += 4 + j;
                            }

                            s += 30;
                            break;
                        default:
                            s += 8;
                            break;
                    }

                    s += 100;
                }

                return s;
            });

    [SkippableTheory(Timeout = 60000)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task DoWhileWithABarrier_MatchesReference(int uValue) =>
        await RunBarrierCase(
            LabeledJumpDifferentialKernels.LaunchDoWhileWithABarrier,
            uValue,
            static (_, x, u) =>
            {
                int s = 0;
                int n = 0;

                outer:
                do
                {
                    for (int j = 0; j < 3; j++)
                    {
                        if (j == 1 && ((n + u) & 1) == 0)
                        {
                            s += 10 + n;
                            continue outer;
                        }

                        if (n == 2 && j == 2)
                        {
                            s += 100 + (x & 3);
                            break outer;
                        }

                        s += n + j + 1;
                    }

                    s += 40;
                } while (++n < 4);

                return (n * 1000) + s;
            });

    [SkippableTheory(Timeout = 60000)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ForEachWithABarrier_MatchesReference(int uValue) =>
        await Task.Run(() => DifferentialRunner.Run(
            DiffCases.Create(
                LabeledJumpDifferentialKernels.LaunchForEachWithABarrier,
                inputs: (rng, rows) => new DiffBuffers()
                    .In("xs", CreateInputs(rng, rows, uValue))
                    .In("items", [2 + uValue, 5, 4 + (uValue & 1), 7 + (uValue >> 1)], OutputPolicy.Exact, readOnly: true)
                    .Out<int>("r", rows, OutputPolicy.Exact),
                shape: s_shape,
                firstInputName: "xs",
                secondInputName: "items",
                outputName: "r",
                reference: static (b, _) =>
                {
                    var xs = b.Get<int>("xs");
                    var items = b.Get<int>("items");
                    var r = b.Get<int>("r");
                    for (int tid = 0; tid < xs.Length; tid++)
                    {
                        int x = xs[tid] & 0x3FF;
                        int s = 0;

                        outer:
                        foreach (int v in items)
                        {
                            for (int j = 0; j < 2; j++)
                            {
                                if (j == 0 && (v & 1) == 0)
                                {
                                    s += (v * 3) + (x & 1);
                                    continue outer;
                                }

                                if (j == 1 && v >= 6)
                                {
                                    s += v * 7;
                                    break outer;
                                }

                                s += v + j;
                            }

                            s += 15;
                        }

                        r[tid] = s;
                    }
                }))).WaitAsync(TimeSpan.FromSeconds(60));

    [SkippableTheory(Timeout = 60000)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ForUnrollBarrierInALabeledLoop_MatchesReference(int uValue) =>
        await RunBarrierCase(
            LabeledJumpDifferentialKernels.LaunchForUnrollBarrierInALabeledLoop,
            uValue,
            static (_, x, u) =>
            {
                int s = 0;

                outer:
                for (int i = 0; i < 3; i++)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        s += k + 1;
                    }

                    for (int j = 0; j < 3; j++)
                    {
                        if (j == 1 && ((i + u) & 1) == 0)
                        {
                            s += 10 + (x & 3);
                            continue outer;
                        }

                        if (i == 2 && j == 2)
                        {
                            s += 100;
                            break outer;
                        }

                        s += j + 1;
                    }

                    s += 50;
                }

                return s;
            });

    [SkippableTheory(Timeout = 60000)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SuspendingCallInALabeledLoop_MatchesReference(int uValue) =>
        await RunBarrierCase(
            LabeledJumpDifferentialKernels.LaunchSuspendingCallInALabeledLoop,
            uValue,
            static (_, x, u) =>
            {
                int v = 0;

                outer:
                for (int i = 0; i < 3; i++)
                {
                    v += 1;
                    for (int j = 0; j < 3; j++)
                    {
                        if (j == 1 && ((i + u) & 1) == 1)
                        {
                            v += 10 + (x & 3);
                            continue outer;
                        }

                        if (i == 2 && j == 2)
                        {
                            v += 100;
                            break outer;
                        }

                        v += (j + 1) * 1000;
                    }

                    v += 50;
                }

                return v;
            });

    [SkippableTheory(Timeout = 60000)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SyncGridBodyInALabeledLoop_MatchesReference(int uValue) =>
        await Task.Run(() => DifferentialRunner.Run(
            DiffCases.Create(
                LabeledJumpDifferentialKernels.LaunchSyncGridBodyInALabeledLoop,
                inputs: (rng, s) => new DiffBuffers()
                    .In("xs", CreateInputs(rng, s.ThreadCount, uValue))
                    .Out<int>("r", s.ThreadCount, OutputPolicy.Exact),
                shapes: [LaunchShapes.Grid1D(2u, 32u)],
                inputName: "xs",
                outputName: "r",
                reference: static (b, _) =>
                {
                    var xs = b.Get<int>("xs");
                    var r = b.Get<int>("r");
                    for (int gid = 0; gid < xs.Length; gid++)
                    {
                        int x = xs[gid] & 0x3FF;
                        int u = xs[0] & 7;
                        int s = 0;
                        int bodyRuns = 0;

                        outer:
                        for (int i = 0; i < 4; i++)
                        {
                            bodyRuns++;
                            for (int j = 0; j < 3; j++)
                            {
                                if (j == 1 && i == (u & 1))
                                {
                                    s += 10 + (x & 3);
                                    continue outer;
                                }

                                if (i == 3 && j == 1)
                                {
                                    s += 200;
                                    break outer;
                                }

                                s += (i * 3) + j + 1;
                            }

                            s += 50;
                        }

                        r[gid] = bodyRuns + (s * 100);
                    }
                }))).WaitAsync(TimeSpan.FromSeconds(60));

    [SkippableTheory(Timeout = 60000)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task WarpShuffleInTheLabeledLoopCondition_MatchesReference(int uValue) =>
        await RunBarrierCase(
            LabeledJumpDifferentialKernels.LaunchWarpShuffleInTheLabeledLoopCondition,
            uValue,
            static (_, x, u) =>
            {
                int s = 0;

                outer:
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        if (j == 1 && ((i + u) & 1) == 0)
                        {
                            s += 10 + (x & 3);
                            continue outer;
                        }

                        if (i == 3 && j == 2)
                        {
                            s += 100;
                            break outer;
                        }

                        s += j + 1;
                    }

                    s += 50;
                }

                return s;
            });

    private static async Task RunBarrierCase(
        Int32PairLaunch launch,
        int uValue,
        Func<int, int, int, int> reference,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        await Task.Run(() => DifferentialRunner.Run(
            DiffCases.Create(
                launch,
                inputs: (rng, rows) => new DiffBuffers()
                    .In("xs", CreateInputs(rng, rows, uValue))
                    .Out<int>("r", rows, OutputPolicy.Exact),
                shape: s_shape,
                inputName: "xs",
                outputName: "r",
                reference: (b, _) =>
                {
                    var xs = b.Get<int>("xs");
                    var r = b.Get<int>("r");
                    for (int tid = 0; tid < xs.Length; tid++)
                    {
                        r[tid] = reference(tid, xs[tid] & 0x3FF, xs[0] & 7);
                    }
                },
                launchExpression: launchExpression))).WaitAsync(TimeSpan.FromSeconds(60));

    private static DifferentialCase CreateSingleColumn(
        Int32PairLaunch launch,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: static (rng, rows) => new DiffBuffers()
                .In("xs", CreateInputs(rng, rows))
                .Out<int>("r", rows, OutputPolicy.Exact),
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

    private static int[] CreateInputs(DiffRandom rng, int rows, int uValue)
    {
        var xs = CreateInputs(rng, rows);
        xs[0] = uValue;
        return xs;
    }
}
