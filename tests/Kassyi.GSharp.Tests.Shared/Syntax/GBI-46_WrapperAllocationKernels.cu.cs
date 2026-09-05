using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class WrapperAllocationKernels
{
    [GpuKernel]
    public static void AllocationTestKernel(
        ref int ioVal,
        out float outVal,
        int[] arrayVal,
        [JitConstant] uint constantVal)
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
