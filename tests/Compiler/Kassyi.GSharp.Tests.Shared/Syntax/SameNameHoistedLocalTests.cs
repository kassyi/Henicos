using System;
#if GSHARP_CPU
using Kassyi.GSharp.Runtime.Aot;
#endif
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Locals of the same name in sibling scopes keep separate context fields when they live across a suspension.</summary>
[Category(TestCategory.Syntax)]
public class SameNameHoistedLocalTests : TranspilerTestBase
{
    private const int Length = 64;
    private const int PlainLoopBound = 40;

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SiblingLoopBarrierKernel_KeepsEachLoopLocal() =>
        AssertSiblingLoopBarrier(static (first, second) => SameNameHoistedLocalKernels.LaunchSiblingLoopBarrierKernel(first, second, blockDimX: Length));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Test_PartlyHoistedNameKernel_KeepsEachBlockLocal() =>
        AssertPartlyHoistedName(static (first, second) => SameNameHoistedLocalKernels.LaunchPartlyHoistedNameKernel(first, second, blockDimX: Length));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_WarpPrefixAndTotalKernel_ComputesPrefixAndTotalPerWarp() =>
        AssertWarpPrefixAndTotal(static (first, second) => SameNameHoistedLocalKernels.LaunchWarpPrefixAndTotalKernel(first, second, blockDimX: Length));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Test_PlainLoopBesideBarrierLoopKernel_StartsPlainLoopAtItsInitializer() =>
        AssertPlainLoopBesideBarrierLoop(static data => SameNameHoistedLocalKernels.LaunchPlainLoopBesideBarrierLoopKernel(data, PlainLoopBound, blockDimX: Length));

#if GSHARP_CPU
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_SiblingLoopBarrierKernel_KeepsEachLoopLocal() =>
        RunManaged(() => AssertSiblingLoopBarrier(static (first, second) => SameNameHoistedLocalKernels.LaunchSiblingLoopBarrierKernel(first, second, blockDimX: Length)));

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_PartlyHoistedNameKernel_KeepsEachBlockLocal() =>
        RunManaged(() => AssertPartlyHoistedName(static (first, second) => SameNameHoistedLocalKernels.LaunchPartlyHoistedNameKernel(first, second, blockDimX: Length)));

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_WarpPrefixAndTotalKernel_ComputesPrefixAndTotalPerWarp() =>
        RunManaged(() => AssertWarpPrefixAndTotal(static (first, second) => SameNameHoistedLocalKernels.LaunchWarpPrefixAndTotalKernel(first, second, blockDimX: Length)));

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_PlainLoopBesideBarrierLoopKernel_StartsPlainLoopAtItsInitializer() =>
        RunManaged(() => AssertPlainLoopBesideBarrierLoop(static data => SameNameHoistedLocalKernels.LaunchPlainLoopBesideBarrierLoopKernel(data, PlainLoopBound, blockDimX: Length)));

    private static void RunManaged(Action run)
    {
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            run();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }
#endif

    private static void AssertSiblingLoopBarrier(Action<int[], uint[]> launch)
    {
        var data = CreateSequence(Length);
        var unsignedData = CreateUnsignedSequence(Length);

        launch(data, unsignedData);

        for (int i = 0; i < Length; i++)
        {
            // The first loop adds 0 and 1; the second loop maps v to 2v, 4v + 1 and 8v + 4
            Assert.Equal(i * 7 + 3 + 1, data[i]);
            Assert.Equal((uint)(i * 5 + 2) * 8u + 4u, unsignedData[i]);
        }
    }

    private static void AssertPlainLoopBesideBarrierLoop(Action<int[]> launch)
    {
        var data = CreateSequence(Length);

        launch(data);

        // The plain loop adds 3 + 19 + 35; the barrier loop adds that total twice, plus 0 and 1
        const int total = 3 + 19 + 35;
        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(i * 7 + 3 + total * 2 + 1, data[i]);
        }
    }

    private static void AssertPartlyHoistedName(Action<int[], uint[]> launch)
    {
        var data = CreateSequence(Length);
        var unsignedData = CreateUnsignedSequence(Length);

        launch(data, unsignedData);

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal((i * 7 + 3) * 3, data[i]);
            Assert.Equal((uint)(i * 5 + 2) + 5u, unsignedData[i]);
        }
    }

    private static void AssertWarpPrefixAndTotal(Action<int[], int[]> launch)
    {
        var input = new int[Length];
        for (int i = 0; i < Length; i++)
        {
            input[i] = i % 7 + 1;
        }

        var output = new int[Length];

        launch(input, output);

        for (int i = 0; i < Length; i++)
        {
            int warpStart = i - i % 32;
            int prefix = 0;
            for (int j = warpStart; j <= i; j++)
            {
                prefix += input[j];
            }

            int total = 0;
            for (int j = warpStart; j < warpStart + 32; j++)
            {
                total += input[j];
            }

            Assert.Equal(prefix * 1000 + total, output[i]);
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

    private static uint[] CreateUnsignedSequence(int length)
    {
        var values = new uint[length];
        for (int i = 0; i < length; i++)
        {
            values[i] = (uint)(i * 5 + 2);
        }

        return values;
    }
}
