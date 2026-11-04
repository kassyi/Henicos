using System.Linq;
#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
#endif
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class StackLinqStaticFormTests
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(new[] { 1, 2, 3, 4, 5, 6 }, 4)]
    [InlineData(new[] { -3, 0, 2, 4, 7 }, 3)]
    public void Test_StaticFusedChain_MatchesLinq(int[] data, int limit)
    {
        var results = new int[3];

        StackLinqStaticFormKernels.LaunchStaticFusedChainKernel(data, limit, results, blockDimX: 1);

        Assert.Equal(data.Sum(), results[0]);
        Assert.Equal(data.Where(x => x > 2).Select(x => x * 10).Sum(), results[1]);
        Assert.Equal(data.Take(limit).Count(x => x % 2 == 0), results[2]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(new[] { 2 }, 0)]
    [InlineData(new[] { 5 }, 0)]
    [InlineData(new[] { 5 }, 1)]
    [InlineData(new[] { -1, 0, 2 }, 2)]
    public void Test_StaticFusedChain_BoundaryInputs(int[] data, int limit)
    {
        var results = new int[3];

        StackLinqStaticFormKernels.LaunchStaticFusedChainKernel(data, limit, results, blockDimX: 1);

        Assert.Equal(data.Sum(), results[0]);
        Assert.Equal(data.Where(x => x > 2).Select(x => x * 10).Sum(), results[1]);
        Assert.Equal(data.Take(limit).Count(x => x % 2 == 0), results[2]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(new[] { 2, 4, 6, 8 }, 6, 1)]
    [InlineData(new[] { 2, 4, 6, 8 }, 5, 0)]
    [InlineData(new[] { 7 }, 7, 1)]
    public void Test_StaticContainsAndAggregate_MatchesLinq(int[] data, int target, int expectedContains)
    {
        var results = new int[3];

        StackLinqStaticFormKernels.LaunchStaticContainsAndAggregateKernel(data, target, results, blockDimX: 1);

        Assert.Equal(expectedContains, results[0]);
        Assert.Equal(data.Aggregate((acc, x) => acc + x), results[1]);
        Assert.Equal(data.Aggregate(100, (acc, x) => acc + x), results[2]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(new[] { 1, 2, 3, 4, 5 }, 2)]
    [InlineData(new[] { 1, 2, 3, 4, 5 }, 0)]
    [InlineData(new[] { 10 }, 5)]
    public void Test_StaticUnfusedChain_MatchesLinq(int[] data, int takeCount)
    {
        var results = new int[2];

        StackLinqStaticFormKernels.LaunchStaticUnfusedChainKernel(data, takeCount, results, blockDimX: 1);

        var expectedSum = data.Where((x, idx) => idx % 2 == 0).Select(x => x + 1).Take(takeCount).Sum();
        var expectedElement = data.Skip(1).Select((x, idx) => x * 10 + idx).ElementAtOrDefault(1);
        Assert.Equal(expectedSum, results[0]);
        Assert.Equal(expectedElement, results[1]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(new[] { -2, 1, 2, 3, 4 })]
    [InlineData(new[] { 0, 5 })]
    public void Test_MixedChainAndReorderedNonInterferingNamedArgs(int[] data)
    {
        var results = new int[3];

        StackLinqStaticFormKernels.LaunchMixedAndNamedArgsKernel(data, results, blockDimX: 1);

        Assert.Equal(data.Where(x => x > 1).Select(x => x * 3).Sum(), results[0]);
        Assert.Equal(data.Where(x => x >= 3).Sum(), results[1]);
        Assert.Equal(data.Where(x => x > 0).Take(3).Aggregate(1, (acc, x) => acc * 2 + x), results[2]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-2)]
    public void Test_StackAllocSpanStatic_MatchesExpected(int seed)
    {
        var results = new int[2];

        StackLinqStaticFormKernels.LaunchStackAllocSpanStaticKernel(seed, results, blockDimX: 1);

        int[] values = [seed, seed + 1, seed + 2, seed + 3];
        Assert.Equal(values.Where(x => x % 2 == 0).Sum(), results[0]);
        Assert.Equal(values.Skip(1).Count(x => x > seed), results[1]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(4)]
    [InlineData(12)]
    public void Test_InterferingExtensionReceiver_EvaluatesReceiverBeforeArguments(int initial)
    {
        var results = new int[3];

        StackLinqStaticFormKernels.LaunchInterferingExtensionReceiverKernel(initial, results, blockDimX: 1);

        Assert.Equal(initial * 100 + (initial + 5), results[0]);
        Assert.Equal(20 * 100 + 20 * 10 + 1, results[1]);
        Assert.Equal(10 * 1000 + 100, results[2]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ReorderedStackLinq_SequencesInterferingArgumentsInWrittenOrder()
    {
        int[] data = [10, 20, 30, 40, 50];
        var results = new int[2];

        StackLinqStaticFormKernels.LaunchReorderedStackLinqKernel(data, 1, results, blockDimX: 1);

        Assert.Equal(30, results[0]);
        Assert.Equal(2, results[1]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InterferingChainedUnfused_SequencesArgumentsInWrittenOrder()
    {
        int[] data = [10, 20, 30, 40, 50];
        var results = new int[2];

        StackLinqStaticFormKernels.LaunchInterferingChainedUnfusedKernel(data, 1, results, blockDimX: 1);

        Assert.Equal(50, results[0]);
        Assert.Equal(2, results[1]);
    }

#if GSHARP_CPU
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_StaticAndMixedForms()
    {
        int[] data = [1, 2, 3, 4, 5];
        var fusedResults = new int[3];
        var mixedResults = new int[3];
        var orderResults = new int[3];
        var reorderedResults = new int[2];
        var chainedResults = new int[2];

        ManagedKernelRun.Run(() =>
        {
            StackLinqStaticFormKernels.LaunchStaticFusedChainKernel(data, 4, fusedResults, blockDimX: 1);
            StackLinqStaticFormKernels.LaunchMixedAndNamedArgsKernel(data, mixedResults, blockDimX: 1);
            StackLinqStaticFormKernels.LaunchInterferingExtensionReceiverKernel(4, orderResults, blockDimX: 1);
            StackLinqStaticFormKernels.LaunchReorderedStackLinqKernel(data, 1, reorderedResults, blockDimX: 1);
            StackLinqStaticFormKernels.LaunchInterferingChainedUnfusedKernel(data, 1, chainedResults, blockDimX: 1);
        });

        Assert.Equal([15, 120, 2], fusedResults);
        Assert.Equal([42, 12, 19], mixedResults);
        Assert.Equal([409, 2201, 10100], orderResults);
        Assert.Equal([3, 2], reorderedResults);
        Assert.Equal([5, 2], chainedResults);
    }
#endif
}
