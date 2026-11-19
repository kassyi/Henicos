#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
public class StackModelTests
{
#if GSHARP_MANAGED_STACK
    private const bool BuiltWithStackModel = true;
#else
    private const bool BuiltWithStackModel = false;
#endif

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ReverseKernel_ManagedExecution_ReversesInput()
    {
        const int n = 16;
        var input = CreateSequence(n);
        var output = new int[n];

        RunManaged(() => StackModelKernels.LaunchReverseKernel(input, output, n, blockDimX: n));

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(input[n - 1 - i], output[i]);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceFunctionKernel_ManagedExecution_ReturnsValueAcrossBarrier()
    {
        const int n = 16;
        var input = CreateSequence(n);
        var output = new int[n];

        RunManaged(() => StackModelKernels.LaunchDeviceFunctionKernel(input, output, n, blockDimX: n));

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(input[n - 1 - i], output[i]);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ReturningKernel_ManagedExecution_ReturnsPerThreadResults()
    {
        const int n = 16;
        var input = CreateSequence(n);

        var results = RunManaged(() => StackModelKernels.LaunchReturningKernel(input, n, blockDimX: n));

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(input[n - 1 - i] + i, results[i]);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void MutatedValueParameterKernel_ManagedExecution_PreservesMutationAcrossBarrier()
    {
        const int n = 16;
        var output = new int[n];

        RunManaged(() => StackModelKernels.LaunchMutatedValueParameterKernel(output, 10, blockDimX: n));

        for (int i = 0; i < n; i++)
        {
            Assert.Equal((10 + i + 1) * 3, output[i]);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void LoopBarrierKernel_ManagedExecution_CarriesLocalsAcrossLoopBarriers()
    {
        const int n = 8;
        const int rounds = 3;
        var input = CreateSequence(n);
        var expected = (int[])input.Clone();
        for (int r = 0; r < rounds; r++)
        {
            var next = new int[n];
            for (int i = 0; i < n; i++)
            {
                next[i] = expected[i] + expected[(i + 1) % n];
            }

            expected = next;
        }

        var output = new int[n];
        RunManaged(() => StackModelKernels.LaunchLoopBarrierKernel(input, output, rounds, n, blockDimX: n));

        Assert.Equal(expected, output);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RefOutKernel_ManagedExecution_PropagatesRefAndOutParameters()
    {
        const int n = 16;
        var input = CreateSequence(n);
        var output = new int[n];
        int refTotal = 100;
        int outValue = 0;

        RunManaged(() => StackModelKernels.LaunchRefOutKernel(input, output, ref refTotal, out outValue, n, blockDimX: n));

        for (int i = 0; i < n; i++)
        {
            int v = input[n - 1 - i];
            Assert.Equal(i + v + v * 2, output[i]);
        }

        Assert.Equal(100 + input[n - 1], refTotal);
        Assert.Equal(input[0] + input[n - 1], outValue);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void NestedDeviceFunctionKernel_ManagedExecution_SuspendsThroughTwoLevels()
    {
        const int n = 16;
        var input = CreateSequence(n);
        var output = new int[n];

        RunManaged(() => StackModelKernels.LaunchNestedDeviceFunctionKernel(input, output, n, blockDimX: n));

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(input[n - 1 - i] + 1 + i, output[i]);
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void FaultAfterBarrierKernel_ManagedExecution_ThrowsAndCompletesOtherThreads()
    {
        const int n = 8;
        var input = CreateSequence(n);
        var output = new int[n];

        Assert.Throws<IndexOutOfRangeException>(() =>
            RunManaged(() => StackModelKernels.LaunchFaultAfterBarrierKernel(input, output, n, blockDimX: n)));

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(i == 3 ? 0 : input[n - 1 - i] + 1, output[i]);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BarrierKernel_BuiltWithEitherModel_RunsOnTheSchedulerOfItsModel()
    {
        const int n = 8;
        var input = CreateSequence(n);
        var output = new int[n];
        long before = ManagedStackRunner.LaunchCountOnThisThread;

        RunManaged(() => StackModelKernels.LaunchReverseKernel(input, output, n, blockDimX: n));

        long delta = ManagedStackRunner.LaunchCountOnThisThread - before;
        Assert.Equal(BuiltWithStackModel ? 1L : 0L, delta);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void WarpKernel_BuiltWithEitherModel_StaysOnStateMachine()
    {
        const int n = 32;
        var input = CreateSequence(n);
        var output = new int[n];
        long before = ManagedStackRunner.LaunchCountOnThisThread;

        RunManaged(() => StackModelKernels.LaunchWarpAndBarrierKernel(input, output, blockDimX: n));

        Assert.Equal(0L, ManagedStackRunner.LaunchCountOnThisThread - before);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(input[i ^ 1], output[i]);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CooperativeKernel_BuiltWithEitherModel_StaysOnStateMachine()
    {
        const int n = 8;
        var input = CreateSequence(n);
        var output = new int[n];
        long before = ManagedStackRunner.LaunchCountOnThisThread;

        RunManaged(() => StackModelKernels.LaunchCooperativeBarrierKernel(input, output, n, blockDimX: n));

        Assert.Equal(0L, ManagedStackRunner.LaunchCountOnThisThread - before);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(input[n - 1 - i], output[i]);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void KernelWithoutBarrier_BuiltWithEitherModel_StaysOnStateMachine()
    {
        const int n = 8;
        var input = CreateSequence(n);
        var output = new int[n];
        long before = ManagedStackRunner.LaunchCountOnThisThread;

        RunManaged(() => StackModelKernels.LaunchNoBarrierKernel(input, output, blockDimX: n));

        Assert.Equal(0L, ManagedStackRunner.LaunchCountOnThisThread - before);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(input[i] * 2 + 1, output[i]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LocalFunctionBarrierKernel_BuiltWithStackModel_ReadsAcrossTheBarrier()
    {
        // The state machine does not wait at a barrier inside a local function yet (#2057, resume after a barrier
        // reached through a delegate, a lambda or a local function on the CPU paths); the stack model does
        Skip.IfNot(BuiltWithStackModel, "The state machine does not wait at a barrier inside a local function (#2057).");
        const int n = 16;
        var input = CreateSequence(n);
        var scratch = new int[n];
        var output = new int[n];

        long launches = CountStackLaunches(() =>
            StackModelKernels.LaunchLocalFunctionBarrierKernel(input, scratch, output, n, blockDimX: n));

        Assert.Equal(BuiltWithStackModel ? 1L : 0L, launches);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(input[n - 1 - i] * 3, output[i]);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void UnrolledBarrierKernel_BuiltWithEitherModel_CarriesLocalsAcrossBarriers()
    {
        const int n = 8;
        var input = CreateSequence(n);
        var expected = (int[])input.Clone();
        for (int r = 0; r < 2; r++)
        {
            var next = new int[n];
            for (int i = 0; i < n; i++)
            {
                next[i] = expected[i] + expected[(i + 1) % n];
            }

            expected = next;
        }

        var scratch = new int[n];
        var output = new int[n];
        long launches = CountStackLaunches(() =>
            StackModelKernels.LaunchUnrolledBarrierKernel(input, scratch, output, n, blockDimX: n));

        Assert.Equal(BuiltWithStackModel ? 1L : 0L, launches);
        Assert.Equal(expected, output);
    }

    // The launches of the stack model that <launch> made on this thread
    private static long CountStackLaunches(Action launch)
    {
        long before = ManagedStackRunner.LaunchCountOnThisThread;
        RunManaged(launch);
        return ManagedStackRunner.LaunchCountOnThisThread - before;
    }

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }

    private static T RunManaged<T>(Func<T> launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            return launch();
        }
    }

    private static int[] CreateSequence(int length)
    {
        var values = new int[length];
        for (int i = 0; i < length; i++)
        {
            values[i] = i * 7 + 3;
        }

        return values;
    }
}
