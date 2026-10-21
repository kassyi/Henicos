using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public static partial class ParallelReductionExecutionKernels
{
    [GpuKernel]
    public static void IntParallelSum(int[] data, int[] result)
    {
        int sum = data.AsStackEnumerable().AsParallel().Sum();
        
        if (ThreadIndex.X == 0)
        {
            result[BlockIndex.X] = sum;
        }
    }
}
