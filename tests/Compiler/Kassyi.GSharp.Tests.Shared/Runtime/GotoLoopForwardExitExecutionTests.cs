#nullable enable
using Kassyi.GSharp.Tests.Shared.Differential;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.DeviceExclusive)]
public class GotoLoopForwardExitExecutionTests
{
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ForwardGotoBeforeGateInEarlierIf_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts()
    {
        var paths = DiffPaths.Available;
        Skip.If(paths.Count == 0, "No execution path available.");

        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[224];
            path.Run(() => GotoLoopForwardExitExecutionKernels.LaunchForwardGotoBeforeGateInEarlierIfKernel(
                result, blockDimX: 32));

            for (var t = 0; t < 32; t++)
            {
                Assert.Equal(0xFFFFFFFFu, result[t + 160]);
                var expectedCount = (t & 4) == 0
                    ? ((t & 8) != 0 && (t & 3) != 0 ? 3u : 0u)
                    : ((t & 16) == 0 ? 2u : 0u);
                Assert.Equal(expectedCount, result[t + 192]);
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ForwardGotoBeforeGateInDeviceFunction_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts()
    {
        var paths = DiffPaths.Available;
        Skip.If(paths.Count == 0, "No execution path available.");

        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[224];
            path.Run(() => GotoLoopForwardExitExecutionKernels.LaunchForwardGotoBeforeGateInDeviceFunctionKernel(
                result, blockDimX: 32));

            for (var t = 0; t < 32; t++)
            {
                Assert.Equal(0xFFFFFFFFu, result[t + 160]);
                var expectedCount = t < 16
                    ? ((t & 4) == 0
                        ? ((t & 8) != 0 && (t & 3) != 0 ? 3u : 0u)
                        : 2u)
                    : 0u;
                Assert.Equal(expectedCount, result[t + 192]);
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ForwardGotoToOuterLabelBeforeLast_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts()
    {
        var paths = DiffPaths.Available;
        Skip.If(paths.Count == 0, "No execution path available.");

        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[224];
            path.Run(() => GotoLoopForwardExitExecutionKernels.LaunchForwardGotoToOuterLabelBeforeLastKernel(
                result, blockDimX: 32));

            for (var t = 0; t < 32; t++)
            {
                Assert.Equal(0xFFFFFFFFu, result[t + 160]);
                var expectedCount = (t & 4) == 0 && (t & 3) != 0 ? 3u : 0u;
                Assert.Equal(expectedCount, result[t + 192]);
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ForwardGotoAfterGateImplicitElse_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts()
    {
        var paths = DiffPaths.Available;
        Skip.If(paths.Count == 0, "No execution path available.");

        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[224];
            path.Run(() => GotoLoopForwardExitExecutionKernels.LaunchForwardGotoAfterGateImplicitElseKernel(
                result, blockDimX: 32));

            for (var t = 0; t < 32; t++)
            {
                Assert.Equal(0xFFFFFFFFu, result[t + 160]);
                var expectedCount = (t & 4) == 0 && (t & 3) != 0 ? 3u : 0u;
                Assert.Equal(expectedCount, result[t + 192]);
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ForwardGotoAfterGateBeforeLast_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts()
    {
        var paths = DiffPaths.Available;
        Skip.If(paths.Count == 0, "No execution path available.");

        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[224];
            path.Run(() => GotoLoopForwardExitExecutionKernels.LaunchForwardGotoAfterGateBeforeLastKernel(
                result, blockDimX: 32));

            for (var t = 0; t < 32; t++)
            {
                Assert.Equal(0xFFFFFFFFu, result[t + 160]);
                var expectedCount = (t & 4) == 0 && (t & 3) != 0 ? 3u : 0u;
                Assert.Equal(expectedCount, result[t + 192]);
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SwitchExitAfterGateBeforeLast_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts()
    {
        var paths = DiffPaths.Available;
        Skip.If(paths.Count == 0, "No execution path available.");

        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[224];
            path.Run(() => GotoLoopForwardExitExecutionKernels.LaunchSwitchExitAfterGateBeforeLastKernel(
                result, blockDimX: 32));

            for (var t = 0; t < 32; t++)
            {
                var returnedEarly = (t & 4) == 0 && (t & 8) != 0;
                Assert.Equal(returnedEarly ? 0u : 0xF0FFF0FFu, result[t + 160]);
                var expectedCount = returnedEarly
                    ? 0u
                    : ((t & 4) == 0
                        ? ((t & 3) != 0 ? 3u : 0u)
                        : ((t & 16) == 0 ? 2u : 0u));
                Assert.Equal(expectedCount, result[t + 192]);
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ForwardGotoDeadTailAfterGateBeforeLast_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts()
    {
        var paths = DiffPaths.Available;
        Skip.If(paths.Count == 0, "No execution path available.");

        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[224];
            path.Run(() => GotoLoopForwardExitExecutionKernels.LaunchForwardGotoDeadTailAfterGateBeforeLastKernel(
                result, blockDimX: 32));

            for (var t = 0; t < 32; t++)
            {
                Assert.Equal(0u, result[t + 128]);
                Assert.Equal(0xFFFFFFFFu, result[t + 160]);
                var expectedCount = (t & 4) == 0
                    ? ((t & 3) != 0 ? 3u : 0u)
                    : ((t & 16) == 0 ? 2u : 0u);
                Assert.Equal(expectedCount, result[t + 192]);
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ForwardGotoToMemberLabel_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts()
    {
        var paths = DiffPaths.Available;
        Skip.If(paths.Count == 0, "No execution path available.");

        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[224];
            path.Run(() => GotoLoopForwardExitExecutionKernels.LaunchForwardGotoToMemberLabelKernel(
                result, blockDimX: 32));

            for (var t = 0; t < 32; t++)
            {
                Assert.Equal(0xFFFFFFFFu, result[t + 160]);
                var expectedCount = (t & 4) == 0
                    ? ((t & 3) != 0
                        ? ((t & 16) == 0 ? 4u : 3u)
                        : ((t & 16) == 0 ? 2u : 0u))
                    : ((t & 16) == 0 ? 2u : 0u);
                Assert.Equal(expectedCount, result[t + 192]);
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DoFalseAfterGateImplicitElse_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts() =>
        AssertImplicitElseLoopExit(
            static r => GotoLoopForwardExitExecutionKernels.LaunchDoFalseAfterGateImplicitElseKernel(r, blockDimX: 32),
            assertDeadSlotZero: false);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void WhileTrueAfterGateImplicitElse_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts() =>
        AssertImplicitElseLoopExit(
            static r => GotoLoopForwardExitExecutionKernels.LaunchWhileTrueAfterGateImplicitElseKernel(r, blockDimX: 32),
            assertDeadSlotZero: false);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ForEverAfterGateImplicitElse_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts() =>
        AssertImplicitElseLoopExit(
            static r => GotoLoopForwardExitExecutionKernels.LaunchForEverAfterGateImplicitElseKernel(r, blockDimX: 32),
            assertDeadSlotZero: false);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DeadLabelAfterGateImplicitElse_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts() =>
        AssertImplicitElseLoopExit(
            static r => GotoLoopForwardExitExecutionKernels.LaunchDeadLabelAfterGateImplicitElseKernel(r, blockDimX: 32),
            assertDeadSlotZero: true);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DeadLabelInNestedIfAfterGateImplicitElse_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts() =>
        AssertImplicitElseLoopExit(
            static r => GotoLoopForwardExitExecutionKernels.LaunchDeadLabelInNestedIfAfterGateImplicitElseKernel(
                r, blockDimX: 32),
            assertDeadSlotZero: true);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DeadLabelInNestedBlockAfterGateImplicitElse_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts() =>
        AssertImplicitElseLoopExit(
            static r => GotoLoopForwardExitExecutionKernels.LaunchDeadLabelInNestedBlockAfterGateImplicitElseKernel(
                r, blockDimX: 32),
            assertDeadSlotZero: true);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SwitchDeadBreakAfterGateImplicitElse_ExecutedOnEveryPath_CompletesWithExpectedMasksAndCounts()
    {
        var paths = DiffPaths.Available;
        Skip.If(paths.Count == 0, "No execution path available.");

        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[224];
            path.Run(() => GotoLoopForwardExitExecutionKernels.LaunchSwitchDeadBreakAfterGateImplicitElseKernel(
                result, blockDimX: 32));

            for (var t = 0; t < 32; t++)
            {
                AssertSlot3Mask(path.Path, t, result[t + 96]);
                var returnedEarly = (t & 4) == 0 && (t & 8) != 0;
                Assert.Equal(returnedEarly ? 0u : 0xF0FFF0FFu, result[t + 160]);
                var expectedCount = returnedEarly
                    ? 0u
                    : ((t & 4) == 0
                        ? ((t & 3) != 0 ? 3u : 0u)
                        : ((t & 16) == 0 ? 2u : 0u));
                Assert.Equal(expectedCount, result[t + 192]);
            }
        }
    }

    private static void AssertImplicitElseLoopExit(Action<uint[]> launch, bool assertDeadSlotZero)
    {
        var paths = DiffPaths.Available;
        Skip.If(paths.Count == 0, "No execution path available.");

        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[224];
            path.Run(() => launch(result));

            for (var t = 0; t < 32; t++)
            {
                AssertSlot3Mask(path.Path, t, result[t + 96]);
                if (assertDeadSlotZero)
                {
                    Assert.Equal(0u, result[t + 128]);
                }

                Assert.Equal(0xFFFFFFFFu, result[t + 160]);
                var expectedCount = (t & 4) == 0
                    ? ((t & 3) != 0 ? 3u : 0u)
                    : ((t & 16) == 0 ? 2u : 0u);
                Assert.Equal(expectedCount, result[t + 192]);
            }
        }
    }

    private static void AssertSlot3Mask(DiffPath diffPath, int t, uint actual)
    {
        var classic = (t & 4) == 0 ? 0x0F0F0F0Fu : 0u;
        if (diffPath != DiffPath.Cuda)
        {
            Assert.Equal(classic, actual);
            return;
        }

        if (classic == 0u)
        {
            Assert.Equal(0u, actual);
            return;
        }

        Assert.NotEqual(0u, actual & (1u << t));
        Assert.Equal(0u, actual & ~classic);
    }
}
