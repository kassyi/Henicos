using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public partial class StackLinqStaticFormTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ManagedExecution_StaticAndMixedForms_MatchesExpectedResults()
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
}
