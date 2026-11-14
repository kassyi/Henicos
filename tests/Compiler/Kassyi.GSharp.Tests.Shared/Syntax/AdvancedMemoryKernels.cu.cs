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

    // A [GpuReadOnly] array is const on the device: the pointers that pin it, one of its elements, or a var local that
    // holds it, point at const elements; comparing them tells whether two arrays are the same
    [GpuKernel]
    public static unsafe void ReadOnlyPinKernel([GpuReadOnly] uint[] src, [GpuReadOnly] uint[] other, uint[] dest)
    {
        var alias = other;
        var aliasOfAlias = alias;
        fixed (uint* pSrc = src, pElement = &src[1], pOther = other, pAlias = aliasOfAlias)
        {
            dest[0] = *pSrc + pElement[0] + pAlias[1];
            dest[1] = pSrc == pOther ? 1u : 0u;
        }
    }
}
