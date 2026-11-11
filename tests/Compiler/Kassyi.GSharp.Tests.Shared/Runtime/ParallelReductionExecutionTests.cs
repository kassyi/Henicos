using System.Linq;
using Kassyi.GSharp.Stdlib.GpuMath;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public class ParallelReductionExecutionTests
{
    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void ParallelReduction_IntBufferSum_MatchesExpectedSum()
    {
        int[] data = Enumerable.Range(1, 128).ToArray();
        int[] result = [0];

        ParallelReductionExecutionKernels.LaunchIntParallelSum(data, result, blockDimX: 1);

        int expected = data.Sum();
        Assert.Equal(expected, result[0]);
    }
}
