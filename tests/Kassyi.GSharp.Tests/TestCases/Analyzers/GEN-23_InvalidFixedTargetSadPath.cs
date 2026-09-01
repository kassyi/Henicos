// ReSharper disable All
using Kassyi.GSharp.Core;

namespace TestSpace;

public static class InvalidFixedKernelContainer
{
    [GpuKernel]
    public static unsafe void BadFixedKernel(int nonArrayVal)
    {
        fixed (int* p = nonArrayVal)
        {
            *p = 10;
        }
    }
}
