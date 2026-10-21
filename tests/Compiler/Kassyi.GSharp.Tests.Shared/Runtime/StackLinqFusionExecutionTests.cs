using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public class StackLinqFusionExecutionTests
{
    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestSimpleWhereSelectSum()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];

        StackLinqFusionExecutionKernels.LaunchSimpleWhereSelectSum(data, result, blockDimX: 1);

        int expected = data.Where(x => x > 5).Select(x => x * 2).Sum();
        Assert.Equal(expected, result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestMultipleWhere()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];

        StackLinqFusionExecutionKernels.LaunchMultipleWhere(data, result, blockDimX: 1);

        int expected = data.Where(x => x > 5).Where(x => x % 2 == 0).Sum();
        Assert.Equal(expected, result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestMultipleSelect()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];

        StackLinqFusionExecutionKernels.LaunchMultipleSelect(data, result, blockDimX: 1);

        int expected = data.Select(x => x * 2).Select(x => x + 1).Sum();
        Assert.Equal(expected, result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestHellPattern()
    {
        int[] data = Enumerable.Range(1, 100).ToArray();
        int[] result = [0];

        StackLinqFusionExecutionKernels.LaunchHellPattern(data, result, blockDimX: 1);

        int expected = data
            .SkipLast(2)
            .TakeLast(95)
            .Skip(3)
            .Take(90)
            .Where(x => x % 2 == 0)
            .SkipWhile(x => x < 20)
            .TakeWhile(x => x <= 80)
            .Select(x => x * 2)
            .Select(x => x + 1)
            .Where(x => x < 100)
            .Sum();

        Assert.Equal(expected, result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestCount()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestCount(data, result, blockDimX: 1);
        Assert.Equal(data.Count(x => x > 5), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestAny()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestAny(data, result, blockDimX: 1);
        Assert.Equal(data.Any(x => x > 5) ? 1 : 0, result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestAll()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestAll(data, result, blockDimX: 1);
        Assert.Equal(data.All(x => x > 5) ? 1 : 0, result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestFirst()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestFirst(data, result, blockDimX: 1);
        Assert.Equal(data.First(x => x > 5), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestFirstOrDefault()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestFirstOrDefault(data, result, blockDimX: 1);
        Assert.Equal(data.FirstOrDefault(x => x > 100), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestLast()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestLast(data, result, blockDimX: 1);
        Assert.Equal(data.Last(x => x < 5), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestLastOrDefault()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestLastOrDefault(data, result, blockDimX: 1);
        Assert.Equal(data.LastOrDefault(x => x > 100), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestMax()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestMax(data, result, blockDimX: 1);
        Assert.Equal(data.Max(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestMin()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestMin(data, result, blockDimX: 1);
        Assert.Equal(data.Min(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestAverage()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestAverage(data, result, blockDimX: 1);
        Assert.Equal((int)data.Average(x => (double)x), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestAggregate()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestAggregate(data, result, blockDimX: 1);
        Assert.Equal(data.Aggregate((acc, x) => acc + x), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestContains()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestContains(data, result, blockDimX: 1);
        Assert.Equal(data.Contains(5) ? 1 : 0, result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestTake()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestTake(data, result, blockDimX: 1);
        Assert.Equal(data.Take(3).Sum(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestSkip()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestSkip(data, result, blockDimX: 1);
        Assert.Equal(data.Skip(3).Sum(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestTakeWhile()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestTakeWhile(data, result, blockDimX: 1);
        Assert.Equal(data.TakeWhile(x => x < 5).Sum(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestSkipWhile()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestSkipWhile(data, result, blockDimX: 1);
        Assert.Equal(data.SkipWhile(x => x < 5).Sum(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestTakeLast()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestTakeLast(data, result, blockDimX: 1);
        Assert.Equal(data.TakeLast(3).Sum(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestSkipLast()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestSkipLast(data, result, blockDimX: 1);
        Assert.Equal(data.SkipLast(3).Sum(), result[0]);
    }
}


