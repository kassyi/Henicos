// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class AdvancedMemoryKernels
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

    [GpuKernel]
    public static unsafe void PointerDecrementKernel(uint[] src, uint[] dest)
    {
        fixed (uint* pSrc = &src[0], pDest = &dest[0])
        {
            uint* ptrS = pSrc + 2;
            uint* ptrD = pDest + 2;
            for (int i = 0; i < 3; i++)
            {
                *ptrD-- = *ptrS--;
            }
        }
    }

    [GpuKernel]
    public static unsafe void PointerComplexAssignmentKernel(uint[] idx, uint[] val, uint[] dest)
    {
        fixed (uint* pIdx = &idx[0], pVal = &val[0])
        {
            uint* ptrI = pIdx;
            uint* ptrV = pVal;
            for (int i = 0; i < 3; i++)
            {
                dest[*ptrI++] = *ptrV++ + 10u;
            }
        }
    }
}
