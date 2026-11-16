using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Collection(TestCollections.GlobalStateSequential)]
public partial class IteratorTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EnumerateManaged_AllIteratorKernels_MatchesNativeSemantics()
    {
        using (GpuExecution.Use(KernelExecution.Managed))
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

            var cfOut = new int[1];
            IteratorKernels.LaunchConsumerControlFlowKernel(5, 3, -1, cfOut, blockDimX: 1);
            Assert.Equal(39, cfOut[0]);

            var multiOut = new int[1];
            IteratorKernels.LaunchMultiYieldLabelAndShadowKernel(1, 2, multiOut, blockDimX: 1);
            Assert.Equal(1125, multiOut[0]);

            int[] spanData = [10, 20, 30, 40];
            var spanOut = new int[1];
            var dispLog = new int[1];
            IteratorKernels.LaunchUsingAndSpanBreakKernel(spanData, 1, spanOut, dispLog, blockDimX: 1);
            Assert.Equal(30, spanOut[0]);
            Assert.Equal(100, dispLog[0]);

            var namedOut = new int[3];
            var evalLog = new int[4];
            IteratorKernels.LaunchNamedAndDefaultArgsKernel(namedOut, evalLog, blockDimX: 1);
            Assert.Equal([39, 42, 21], namedOut);
            Assert.Equal([1, 2, 3, 4], evalLog);

            var wideningOut = new long[1];
            IteratorKernels.LaunchWideningElementTypeKernel(wideningOut, blockDimX: 1);
            Assert.Equal(int.MaxValue + 1L, wideningOut[0]);

            var inlineArrOut = new int[4];
            IteratorKernels.LaunchInlineArrayForEachKernel(3, inlineArrOut, blockDimX: 4);
            Assert.Equal([30, 42, 54, 66], inlineArrOut);

            var seventeenOut = new int[4];
            IteratorDepthKernels.LaunchSeventeenLevelsKernel(seventeenOut, blockDimX: 4);
            Assert.Equal([17, 18, 19, 20], seventeenOut);
        }
    }
}
