using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class WrapperAllocationKernels
{
    [GpuKernel]
    public static void AllocationTestKernel(
        ref int ioVal,
        out float outVal,
        int[] arrayVal,
        [GpuConstexpr(10, 11, 12)] uint constantVal)
    {
        if (ioVal > constantVal)
        {
            outVal = arrayVal[0];
            ioVal = (int)outVal + 1;
        }
        else
        {
            outVal = 0;
        }
    }
}
