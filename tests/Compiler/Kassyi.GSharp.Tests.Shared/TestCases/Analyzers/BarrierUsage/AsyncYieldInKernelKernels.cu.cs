using System.Collections.Generic;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class AsyncYieldInKernelKernels
{
    // GSH0046: async kernel is prohibited
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static async Task AsyncKernel()
    {
        await Task.Delay(1);
    }

    // GSH0046: yield return kernel is prohibited
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static IEnumerable<int> YieldKernel()
    {
        yield return 42;
    }
}
