#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Tests.Shared.Differential;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// A failed C# check stops the thread where it fails, on every path: nothing that the thread would write after the
/// failing check reaches the output, and the launch throws the exception of the check. The other threads of the launch
/// run on, as on the GPU, where the failing thread exits and the barriers stop counting it. Runs
/// <see cref="ThreadStopKernels"/> on the managed and native paths (Tests.Cpu) and on CUDA (Tests.Cuda).
/// </summary>
/// <remarks>
/// Every output starts filled with <see cref="Sentinel"/>, so an entry that keeps it was never written. The release
/// CUDA build does not check <c>Slice</c> and <c>AsSpan</c>, so those cases run on CUDA only in a checked build. On
/// CUDA a failure inside a <c>using</c> does not dispose the resource yet (#1770), so the cases that need it run on the
/// CPU paths.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Compiler)]
[Collection(TestCollections.DeviceExclusive)]
public class ThreadStopTests
{
    private const int Sentinel = -7;
    private const int S = Sentinel;

    private const DiffPath EveryPath = DiffPath.Managed | DiffPath.Native | DiffPath.Cuda;
    private const DiffPath CpuPaths = DiffPath.Managed | DiffPath.Native;

    private static readonly int[] s_threeElements = [3, 1, 2];

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void SpanSlice_StartPastTheEnd_WritesNothingAfterTheSlice() =>
        AssertThreadStops<ArgumentOutOfRangeException>(
            DiffPaths.CheckedPaths(checkedBuildOnlyOnCuda: true),
            [1, S],
            dst => ThreadStopKernels.LaunchSpanSliceKernel(s_threeElements, dst, 5)
        );

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void SpanSlice_CountPastTheEnd_WritesNothingAfterTheSlice() =>
        AssertThreadStops<ArgumentOutOfRangeException>(
            DiffPaths.CheckedPaths(checkedBuildOnlyOnCuda: true),
            [1, S],
            dst => ThreadStopKernels.LaunchSpanSliceCountKernel(s_threeElements, dst, 1, 5)
        );

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void AsSpan_StartPastTheEnd_WritesNothingAfterTheCall() =>
        AssertThreadStops<ArgumentOutOfRangeException>(
            DiffPaths.CheckedPaths(checkedBuildOnlyOnCuda: true),
            [1, S],
            dst => ThreadStopKernels.LaunchAsSpanStartKernel(s_threeElements, dst, 4)
        );

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void CopyTo_DestinationTooShort_WritesNothingAfterTheCopy() =>
        AssertThreadStops<ArgumentException>(
            EveryPath,
            [1, S],
            dst => ThreadStopKernels.LaunchCopyToShortKernel(s_threeElements, [S, S], dst)
        );

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void First_EmptySequence_DoesNotAssignTheResult() =>
        AssertThreadStops<InvalidOperationException>(
            EveryPath,
            [1, S],
            dst => ThreadStopKernels.LaunchFirstOfEmptyKernel(s_threeElements, dst, 0)
        );

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void FusedFirst_NoMatch_DoesNotAssignTheResult() =>
        AssertThreadStops<InvalidOperationException>(
            EveryPath,
            [1, S],
            dst => ThreadStopKernels.LaunchFusedFirstKernel(s_threeElements, dst, 1000)
        );

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void FusedSelector_FailsInTheLoop_DoesNotWriteTheSum() =>
        AssertThreadStops<InvalidOperationException>(
            EveryPath,
            [1, S],
            dst => ThreadStopKernels.LaunchFusedSelectorFailsKernel([1, -1, 2], dst)
        );

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void DeviceFunction_FailsInside_NeitherItNorTheCallerWritesAfterIt() =>
        AssertThreadStops<ArgumentException>(
            EveryPath,
            [1, S, S, S],
            dst => ThreadStopKernels.LaunchDeviceFunctionKernel(s_threeElements, [S, S], dst)
        );

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void EvaluationOrder_RightOperandFails_KeepsTheLeftOperandsWriteOnly() =>
        AssertThreadStops<InvalidOperationException>(
            EveryPath,
            [1, S],
            dst => ThreadStopKernels.LaunchEvaluationOrderKernel(s_threeElements, dst, 0)
        );

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void AfterBarrier_CheckFails_WritesNothingAfterIt() =>
        AssertThreadStops<InvalidOperationException>(
            EveryPath,
            [1, S, S],
            dst => ThreadStopKernels.LaunchAfterBarrierKernel(s_threeElements, dst, 0)
        );

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void SortComparer_Fails_WritesNothingAfterTheSort() =>
        AssertThreadStops<InvalidOperationException>(
            EveryPath,
            [1, S],
            dst => ThreadStopKernels.LaunchSortComparerFailsKernel([3, 1, 2], dst, 0)
        );

    // C# disposes the resource in a finally as the failure leaves the using
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Using_BodyFails_DisposesAndWritesNothingElse() =>
        AssertThreadStops<InvalidOperationException>(
            CpuPaths,
            [1, 101, S, S, S],
            log => ThreadStopKernels.LaunchUsingBodyFailsKernel(s_threeElements, log, 0)
        );

    // The exception of Dispose replaces the exception of the body, as in C#
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Using_BodyAndDisposeFail_ThrowsTheDisposeFailure() =>
        AssertThreadStops<ArgumentException>(
            CpuPaths,
            [1, S, 30, S, S],
            log => ThreadStopKernels.LaunchUsingBodyAndDisposeFailKernel(s_threeElements, log, 0)
        );

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Using_DisposeFailsAfterTheBody_WritesNothingAfterTheUsing() =>
        AssertThreadStops<ArgumentException>(
            EveryPath,
            [1, S, 30, S, S],
            log => ThreadStopKernels.LaunchUsingDisposeFailsKernel(s_threeElements, log)
        );

    // Two blocks of four threads; thread 0 fails, every other thread writes both of its entries
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void OtherThreads_OneThreadFails_RunToTheirEnd() =>
        AssertThreadStops<InvalidOperationException>(
            EveryPath,
            [1, S, 1, 11, 1, 12, 1, 13, 1, 14, 1, 15, 1, 16, 1, 17],
            dst => ThreadStopKernels.LaunchOtherThreadsRunOnKernel(s_threeElements, dst, 0, gridDimX: 2, blockDimX: 4)
        );

    // The block barrier does not wait for a thread that failed
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void OtherThreads_OneThreadFailsBeforeTheBarrier_PassTheBarrier() =>
        AssertThreadStops<InvalidOperationException>(
            EveryPath,
            [1, S, 1, 11, 1, 12, 1, 13],
            dst => ThreadStopKernels.LaunchOtherThreadsPassBarrierKernel(s_threeElements, dst, 0, blockDimX: 4)
        );

    // A lane that failed has exited and takes no part in the ballot of the others
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Ballot_LaneZeroFailed_CountsTheOtherLanes() =>
        AssertThreadStops<InvalidOperationException>(
            EveryPath,
            [S, .. Enumerable.Repeat(unchecked((int)0xFFFFFFFEu), 31)],
            dst => ThreadStopKernels.LaunchBallotAfterLaneFailsKernel(s_threeElements, dst, 0, blockDimX: 32)
        );

    // Every thread writes before the grid sync; the launch ends there, so no thread writes after it
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void GridSync_OneThreadFailedBeforeIt_EndsTheLaunchThere() =>
        AssertThreadStops<InvalidOperationException>(
            EveryPath,
            [1, 1, 1, 1, 1, 1, 1, 1, S, S, S, S, S, S, S, S],
            dst => ThreadStopKernels.LaunchFailureBeforeGridSyncKernel(
                s_threeElements,
                dst,
                0,
                gridDimX: 2,
                blockDimX: 4
            )
        );

    // A failure inside a generic struct constructor leaves the shared memory of that struct type to the other lanes
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void GenericConstructor_OneLaneFailsInside_OtherLanesKeepItsSharedMemory() =>
        AssertThreadStops<InvalidOperationException>(
            EveryPath,
            [80, S, 80, 80],
            dst => ThreadStopKernels.LaunchFailureInGenericConstructorKernel(s_threeElements, dst, blockDimX: 4)
        );

    // Runs the launch on each path of this build among paths, with an output of expected.Length entries filled with the
    // sentinel, and checks the exception and the output
    private static void AssertThreadStops<TException>(DiffPath paths, int[] expected, Action<int[]> launch)
        where TException : Exception
    {
        var selected = DiffPaths.Available.Where(p => (p.Path & paths) != 0).ToArray();
        Skip.If(selected.Length == 0, "No path of this build checks the failing form");
#if GSHARP_CUDA
        CudaCapability.EnsureGpuAvailable();
#endif
        foreach (var path in selected)
        {
            path.Preflight?.Invoke();
            var output = Enumerable.Repeat(Sentinel, expected.Length).ToArray();
            var exception = Record.Exception(() => path.Run(() => launch(output)));
            var actual = exception?.GetType().Name ?? "no exception";
            Assert.True(exception?.GetType() == typeof(TException),
                $"{path.Name}: expected {typeof(TException).Name}, got {actual}: {exception?.Message}");
            Assert.True(expected.SequenceEqual(output),
                $"{path.Name}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", output)}]");
        }
    }
}
