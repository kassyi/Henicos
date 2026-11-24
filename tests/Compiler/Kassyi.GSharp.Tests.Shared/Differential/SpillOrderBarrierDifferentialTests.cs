#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Differential;

[Collection(TestCollections.GlobalStateSequential)]
public partial class SpillOrderBarrierDifferentialTests : TranspilerTestBase
{
    private const int ThreadCount = 32;

    // -------------------------------------------------------------------------
    // Shape 6 (#1000): Conditional ref write on kernel value parameters across barrier
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_RefConditionalWriteOnKernelValueParameterAcrossBarrier_MatchesExpected()
    {
        int[] expected = new int[ThreadCount];
        for (int tid = 0; tid < ThreadCount; tid++)
        {
            int l = 10;
            int r = 20;
            bool pickLeft = (tid & 1) == 0;
            (pickLeft ? ref l : ref r) += tid + 5;
            expected[tid] = l * 100 + r;
        }

        int[] actual = new int[ThreadCount];
        SpillOrderMutatingGetterKernels.LaunchRefConditionalWriteOnKernelValueParameterAcrossBarrierKernel(
            actual, 10, 20, blockDimX: ThreadCount);
        Assert.Equal(expected, actual);

        RunManagedVerification(() =>
        {
            int[] managedActual = new int[ThreadCount];
            SpillOrderMutatingGetterKernels.LaunchRefConditionalWriteOnKernelValueParameterAcrossBarrierKernel(
                managedActual, 10, 20, blockDimX: ThreadCount);
            Assert.Equal(expected, managedActual);
        });
    }

    // -------------------------------------------------------------------------
    // Shape 7 (#1000): Conditional ref argument on kernel value parameters across barrier
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_RefConditionalArgumentOnKernelValueParameterAcrossBarrier_MatchesExpected()
    {
        int[] expected = new int[ThreadCount];
        for (int tid = 0; tid < ThreadCount; tid++)
        {
            int l = 10;
            int r = 20;
            bool pickLeft = (tid & 1) == 0;
            (pickLeft ? ref l : ref r) += tid + 7;
            expected[tid] = l * 100 + r;
        }

        int[] actual = new int[ThreadCount];
        SpillOrderMutatingGetterKernels.LaunchRefConditionalArgumentOnKernelValueParameterAcrossBarrierKernel(
            actual, 10, 20, blockDimX: ThreadCount);
        Assert.Equal(expected, actual);

        RunManagedVerification(() =>
        {
            int[] managedActual = new int[ThreadCount];
            SpillOrderMutatingGetterKernels.LaunchRefConditionalArgumentOnKernelValueParameterAcrossBarrierKernel(
                managedActual, 10, 20, blockDimX: ThreadCount);
            Assert.Equal(expected, managedActual);
        });
    }

    // -------------------------------------------------------------------------
    // Shape 8 (#1000): Conditional ref write on suspending device function value parameters across barrier
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_RefConditionalWriteOnDeviceValueParameterAcrossBarrier_MatchesExpected()
    {
        int[] expected = new int[ThreadCount];
        for (int tid = 0; tid < ThreadCount; tid++)
        {
            int l = 15;
            int r = 25;
            int selector = tid & 1;
            (selector == 0 ? ref l : ref r) += tid + 3;
            expected[tid] = l * 100 + r;
        }

        int[] actual = new int[ThreadCount];
        SpillOrderMutatingGetterKernels.LaunchRefConditionalWriteOnDeviceValueParameterAcrossBarrierKernel(
            actual, 15, 25, blockDimX: ThreadCount);
        Assert.Equal(expected, actual);

        RunManagedVerification(() =>
        {
            int[] managedActual = new int[ThreadCount];
            SpillOrderMutatingGetterKernels.LaunchRefConditionalWriteOnDeviceValueParameterAcrossBarrierKernel(
                managedActual, 15, 25, blockDimX: ThreadCount);
            Assert.Equal(expected, managedActual);
        });
    }

    // -------------------------------------------------------------------------
    // #944: [GpuOperatorMacro] implicitly passing kernel value parameter by ref across barrier
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_OperatorMacroOnKernelValueParameterAcrossBarrier_PreservesContextMutations()
    {
        var initialState = new Kassyi.GSharp.Tests.Shared.Syntax.OperatorMacroContextState { Offset = 100, Calls = 0 };
        uint[] result = [0];
        uint[] calls = [0];

        SpillOrderMutatingGetterKernels.LaunchOperatorMacroOnKernelValueParameterAcrossBarrierKernel(
            bigCtx: initialState,
            operands: new OperatorMacroOperands { Left = [3], Right = [4] },
            results: new OperatorMacroResults { Result = result, Calls = calls });
        Assert.Equal(107u, result[0]);
        Assert.Equal(1u, calls[0]);

        RunManagedVerification(() =>
        {
            uint[] managedResult = [0];
            uint[] managedCalls = [0];
            SpillOrderMutatingGetterKernels.LaunchOperatorMacroOnKernelValueParameterAcrossBarrierKernel(
                bigCtx: initialState,
                operands: new OperatorMacroOperands { Left = [3], Right = [4] },
                results: new OperatorMacroResults { Result = managedResult, Calls = managedCalls });
            Assert.Equal(107u, managedResult[0]);
            Assert.Equal(1u, managedCalls[0]);
        });
    }

    // -------------------------------------------------------------------------
    // #944: [GpuOperatorMacro] implicitly passing suspending device function value parameter by ref across barrier
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_OperatorMacroOnDeviceValueParameterAcrossBarrier_PreservesContextMutations()
    {
        var initialState = new Kassyi.GSharp.Tests.Shared.Syntax.OperatorMacroContextState { Offset = 50, Calls = 0 };
        uint[] result = [0];
        uint[] calls = [0];

        SpillOrderMutatingGetterKernels.LaunchOperatorMacroOnDeviceValueParameterAcrossBarrierKernel(
            bigCtx: initialState,
            operands: new OperatorMacroOperands { Left = [5], Right = [6] },
            results: new OperatorMacroResults { Result = result, Calls = calls });
        Assert.Equal(61u, result[0]);
        Assert.Equal(1u, calls[0]);

        RunManagedVerification(() =>
        {
            uint[] managedResult = [0];
            uint[] managedCalls = [0];
            SpillOrderMutatingGetterKernels.LaunchOperatorMacroOnDeviceValueParameterAcrossBarrierKernel(
                bigCtx: initialState,
                operands: new OperatorMacroOperands { Left = [5], Right = [6] },
                results: new OperatorMacroResults { Result = managedResult, Calls = managedCalls });
            Assert.Equal(61u, managedResult[0]);
            Assert.Equal(1u, managedCalls[0]);
        });
    }

    // -------------------------------------------------------------------------
    // F14: unsafe { ... Gpu.SyncThreads(); ... } flattened by BranchRewriter
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_UnsafeBlockWithSyncThreads_SplitsStateMachineCorrectly()
    {
        int[] expected = new int[ThreadCount];
        for (int tid = 0; tid < ThreadCount; tid++)
        {
            int peer = (tid + 1) & 31;
            expected[tid] = peer + 10;
        }

        int[] actual = new int[ThreadCount];
        SpillOrderMutatingGetterKernels.LaunchUnsafeBlockWithSyncThreadsKernel(
            actual, blockDimX: ThreadCount);
        Assert.Equal(expected, actual);

        RunManagedVerification(() =>
        {
            int[] managedActual = new int[ThreadCount];
            SpillOrderMutatingGetterKernels.LaunchUnsafeBlockWithSyncThreadsKernel(
                managedActual, blockDimX: ThreadCount);
            Assert.Equal(expected, managedActual);
        });
    }

    // -------------------------------------------------------------------------
    // F14: unsafe { ... Warp.ShuffleSync(...); ... } flattened by BranchRewriter
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_UnsafeBlockWithWarpShuffle_SplitsStateMachineCorrectly()
    {
        int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => 100 + tid).ToArray();

        int[] actual = new int[ThreadCount];
        SpillOrderMutatingGetterKernels.LaunchUnsafeBlockWithWarpShuffleKernel(
            actual, 100, blockDimX: ThreadCount);
        Assert.Equal(expected, actual);

        RunManagedVerification(() =>
        {
            int[] managedActual = new int[ThreadCount];
            SpillOrderMutatingGetterKernels.LaunchUnsafeBlockWithWarpShuffleKernel(
                managedActual, 100, blockDimX: ThreadCount);
            Assert.Equal(expected, managedActual);
        });
    }

    // -------------------------------------------------------------------------
    // A ref conditional read as a value keeps its read, so the call after it is sequenced
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_RefConditionalValueReadOneLevel_SequencedCorrectly()
    {
        int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => (tid + 9) * 1000 + (tid + 9 + 100)).ToArray();

        int[] actual = new int[ThreadCount];
        SpillOrderMutatingGetterKernels.LaunchRefConditionalValueReadOneLevelKernel(
            actual, 7, blockDimX: ThreadCount);
        Assert.Equal(expected, actual);

        RunManagedVerification(() =>
        {
            int[] managedActual = new int[ThreadCount];
            SpillOrderMutatingGetterKernels.LaunchRefConditionalValueReadOneLevelKernel(
                managedActual, 7, blockDimX: ThreadCount);
            Assert.Equal(expected, managedActual);
        });
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_RefConditionalValueReadNested_SequencedCorrectly()
    {
        int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => (tid + 9) * 1000 + (tid + 9 + 100)).ToArray();

        int[] actual = new int[ThreadCount];
        SpillOrderMutatingGetterKernels.LaunchRefConditionalValueReadNestedKernel(
            actual, 7, blockDimX: ThreadCount);
        Assert.Equal(expected, actual);

        RunManagedVerification(() =>
        {
            int[] managedActual = new int[ThreadCount];
            SpillOrderMutatingGetterKernels.LaunchRefConditionalValueReadNestedKernel(
                managedActual, 7, blockDimX: ThreadCount);
            Assert.Equal(expected, managedActual);
        });
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_RefConditionalValueReadNestedWithBarrier_SequencedCorrectly()
    {
        int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => (tid + 9) * 1000 + (tid + 9 + 100)).ToArray();

        int[] actual = new int[ThreadCount];
        SpillOrderMutatingGetterKernels.LaunchRefConditionalValueReadNestedWithBarrierKernel(
            actual, 7, blockDimX: ThreadCount);
        Assert.Equal(expected, actual);

        RunManagedVerification(() =>
        {
            int[] managedActual = new int[ThreadCount];
            SpillOrderMutatingGetterKernels.LaunchRefConditionalValueReadNestedWithBarrierKernel(
                managedActual, 7, blockDimX: ThreadCount);
            Assert.Equal(expected, managedActual);
        });
    }

    partial void RunManagedVerification(Action action);
}
