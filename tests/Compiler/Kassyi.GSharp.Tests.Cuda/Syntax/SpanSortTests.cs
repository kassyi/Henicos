using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cuda.Syntax;

public class SpanSortTests
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortSpanKernel_SortsAscending()
    {
        int[] data = [5, 3, 9, 1, 7, 3];

        SpanSortKernels.LaunchSortSpanKernel(data, blockDimX: 1);

        Assert.Equal([1, 3, 3, 5, 7, 9], data);
    }
}
