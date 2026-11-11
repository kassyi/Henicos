using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public class StackLinqFusionExecutionTests
{
    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_SimpleWhereSelectSum_MatchesReference()
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
    public void Execute_MultipleWhereFilters_MatchesReference()
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
    public void Execute_MultipleSelectProjections_MatchesReference()
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
    public void Execute_ComplexChainedPipeline_MatchesReference()
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
    public void Execute_CountPredicate_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestCount(data, result, blockDimX: 1);
        Assert.Equal(data.Count(x => x > 5), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_AnyPredicate_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestAny(data, result, blockDimX: 1);
        Assert.Equal(data.Any(x => x > 5) ? 1 : 0, result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_AllPredicate_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestAll(data, result, blockDimX: 1);
        Assert.Equal(data.All(x => x > 5) ? 1 : 0, result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_FirstPredicate_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestFirst(data, result, blockDimX: 1);
        Assert.Equal(data.First(x => x > 5), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_FirstOrDefaultPredicate_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestFirstOrDefault(data, result, blockDimX: 1);
        Assert.Equal(data.FirstOrDefault(x => x > 100), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_LastPredicate_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestLast(data, result, blockDimX: 1);
        Assert.Equal(data.Last(x => x < 5), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_LastOrDefaultPredicate_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestLastOrDefault(data, result, blockDimX: 1);
        Assert.Equal(data.LastOrDefault(x => x > 100), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_Max_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestMax(data, result, blockDimX: 1);
        Assert.Equal(data.Max(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_Min_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestMin(data, result, blockDimX: 1);
        Assert.Equal(data.Min(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_Average_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestAverage(data, result, blockDimX: 1);
        Assert.Equal((int)data.Average(x => (double)x), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_AggregateCustomAccumulator_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestAggregate(data, result, blockDimX: 1);
        Assert.Equal(data.Aggregate((acc, x) => acc + x), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_ContainsValue_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestContains(data, result, blockDimX: 1);
        Assert.Equal(data.Contains(5) ? 1 : 0, result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_TakeCount_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestTake(data, result, blockDimX: 1);
        Assert.Equal(data.Take(3).Sum(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_SkipCount_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestSkip(data, result, blockDimX: 1);
        Assert.Equal(data.Skip(3).Sum(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_TakeWhilePredicate_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestTakeWhile(data, result, blockDimX: 1);
        Assert.Equal(data.TakeWhile(x => x < 5).Sum(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_SkipWhilePredicate_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestSkipWhile(data, result, blockDimX: 1);
        Assert.Equal(data.SkipWhile(x => x < 5).Sum(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_TakeLastCount_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestTakeLast(data, result, blockDimX: 1);
        Assert.Equal(data.TakeLast(3).Sum(), result[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Execute_SkipLastCount_MatchesReference()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int[] result = [0];
        StackLinqFusionExecutionKernels.LaunchTestSkipLast(data, result, blockDimX: 1);
        Assert.Equal(data.SkipLast(3).Sum(), result[0]);
    }
}


