using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
#endif

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A <c>[GpuDeviceFunction]</c> returning <see cref="System.Collections.Generic.IEnumerable{T}"/> with <c>yield</c>
/// compiles to a value-type enumerator and produces the same sequence on CUDA, CPU AOT, and managed execution.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
#if GSHARP_CPU
[Collection(TestCollections.GlobalStateSequential)]
#endif
public class IteratorTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Range_SumsArithmeticProgressionPerThread()
    {
        const int threads = 8;
        const int count = 4;
        var output = new int[threads];

        IteratorKernels.LaunchRangeKernel(output, count, blockDimX: threads);

        for (int tid = 0; tid < threads; tid++)
        {
            Assert.Equal(4 * tid + 6, output[tid]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Evens_FiltersArrayWithForEachAndYield()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        var output = new int[1];

        IteratorKernels.LaunchEvensKernel(data, data.Length, output, blockDimX: 1);

        Assert.Equal(20, output[0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Fibonacci_PreservesLocalsAcrossYields()
    {
        var lastOutput = new int[1];
        var sumOutput = new int[1];

        IteratorKernels.LaunchFibonacciKernel(10, lastOutput, sumOutput, blockDimX: 1);

        Assert.Equal(34, lastOutput[0]);
        Assert.Equal(88, sumOutput[0]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(5, 10)]
    public void Test_Countdown_EnumeratingSameLocalTwiceRestoresInitialParameter(int n, int expectedSum)
    {
        var firstSum = new int[1];
        var secondSum = new int[1];

        IteratorKernels.LaunchCountdownTwiceKernel(n, firstSum, secondSum, blockDimX: 1);

        Assert.Equal(expectedSum, firstSum[0]);
        Assert.Equal(expectedSum, secondSum[0]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 0)]
    [InlineData(5, 6)]
    [InlineData(100, 21)]
    public void Test_TakeUntil_YieldBreakTerminatesEnumerationEarly(int limit, int expectedSum)
    {
        int[] data = [1, 2, 3, 6, 9];
        var output = new int[1];

        IteratorKernels.LaunchTakeUntilKernel(data, limit, output, blockDimX: 1);

        Assert.Equal(expectedSum, output[0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Doubled_NestedIteratorForEachDoublesRangeSum()
    {
        const int threads = 8;
        const int count = 4;
        var output = new int[threads];

        IteratorKernels.LaunchDoubledKernel(output, count, blockDimX: threads);

        for (int tid = 0; tid < threads; tid++)
        {
            Assert.Equal(2 * (4 * tid + 6), output[tid]);
        }
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0, 0)]
    [InlineData(3, 63)]
    [InlineData(4, 106)]
    public void Test_StructItems_YieldsNestedUserDefinedStructs(int count, int expectedBaseSum)
    {
        const int threads = 4;
        var output = new int[threads];

        IteratorKernels.LaunchStructIteratorKernel(output, count, blockDimX: threads);

        for (int tid = 0; tid < threads; tid++)
        {
            Assert.Equal(expectedBaseSum + count * tid, output[tid]);
        }
    }

#if GSHARP_CPU
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_AllIteratorKernels_MatchNativeSemantics()
    {
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            var rangeOut = new int[4];
            IteratorKernels.LaunchRangeKernel(rangeOut, 4, blockDimX: 4);
            Assert.Equal([6, 10, 14, 18], rangeOut);

            int[] data = [1, 2, 3, 4, 5, 6, 7, 8];
            var evensOut = new int[1];
            IteratorKernels.LaunchEvensKernel(data, data.Length, evensOut, blockDimX: 1);
            Assert.Equal(20, evensOut[0]);

            var fibLast = new int[1];
            var fibSum = new int[1];
            IteratorKernels.LaunchFibonacciKernel(10, fibLast, fibSum, blockDimX: 1);
            Assert.Equal(34, fibLast[0]);
            Assert.Equal(88, fibSum[0]);

            var cFirst = new int[1];
            var cSecond = new int[1];
            IteratorKernels.LaunchCountdownTwiceKernel(5, cFirst, cSecond, blockDimX: 1);
            Assert.Equal(10, cFirst[0]);
            Assert.Equal(10, cSecond[0]);

            int[] takeData = [1, 2, 3, 6, 9];
            var takeOut = new int[1];
            IteratorKernels.LaunchTakeUntilKernel(takeData, 5, takeOut, blockDimX: 1);
            Assert.Equal(6, takeOut[0]);

            var doubledOut = new int[4];
            IteratorKernels.LaunchDoubledKernel(doubledOut, 4, blockDimX: 4);
            Assert.Equal([12, 20, 28, 36], doubledOut);

            var structOut = new int[4];
            IteratorKernels.LaunchStructIteratorKernel(structOut, 3, blockDimX: 4);
            Assert.Equal([63, 66, 69, 72], structOut);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }
#endif
}
