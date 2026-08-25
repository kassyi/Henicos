using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class SYN_31_38_AdvancedMemoryKernels
{
    [GpuKernel]
    public static unsafe void FixedBufferKernel(uint[] data)
    {
        fixed (uint* p = &data[0])
        {
            p[1] = p[0] + 10;
        }
    }

    [GpuKernel]
    public static unsafe void PointerIncrementKernel(uint[] src, uint[] dest)
    {
        fixed (uint* pSrc = &src[0], pDest = &dest[0])
        {
            uint* ptrS = pSrc;
            uint* ptrD = pDest;
            for (int i = 0; i < 3; i++)
            {
                *ptrD++ = *ptrS++;
            }
        }
    }
}
