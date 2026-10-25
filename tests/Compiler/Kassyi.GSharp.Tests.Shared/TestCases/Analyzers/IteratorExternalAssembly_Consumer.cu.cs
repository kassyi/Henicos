using ExternalLib;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

[SkipSnapshotConsistency("Sad Path")]
public static partial class IteratorExternalAssemblyConsumerKernels
{
    [GpuKernel]
    public static void ConsumeExternal(int[] output)
    {
        int sum = 0;
        foreach (var x in ExternalIterators.ExternalRange(4))
        {
            sum += x;
        }

        var localIt = ExternalIterators.ExternalRange(4);
        foreach (var y in localIt)
        {
            sum += y;
        }

        output[0] = sum;
    }
}
