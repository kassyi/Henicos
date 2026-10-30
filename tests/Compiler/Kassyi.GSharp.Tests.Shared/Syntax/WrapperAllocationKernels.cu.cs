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

    // An array launcher with one T[] argument: no other holder can share its array, so it needs no DeviceArrayRegions
    [GpuKernel]
    public static void SingleArrayKernel(int[] a)
    {
        if (ThreadIndex.X == 0)
        {
            a[0] += 1;
        }
    }

    // A struct argument that Marshal_ converts but that holds no array: no array of it can be shared, so the launcher
    // needs no DeviceArrayRegions
    [GpuKernel]
    public static void ArrayFreeStructKernel(ref ArrayFreeCounter counter)
    {
        if (ThreadIndex.X == 0)
        {
            counter.Value += 1;
        }
    }
}

public struct ArrayFreeCounter
{
    public int Value;
}
