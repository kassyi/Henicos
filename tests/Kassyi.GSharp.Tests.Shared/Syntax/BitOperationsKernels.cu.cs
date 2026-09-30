// ReSharper disable All
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static partial class BitOperationsKernels
{
    [GpuKernel]
    public static void Rotate32Kernel(uint[] val, int[] shift, uint[] rotLeftRes, uint[] rotRightRes)
    {
        int tid = ThreadIndex.X;
        rotLeftRes[tid] = BitOperations.RotateLeft(val[tid], shift[tid]);
        rotRightRes[tid] = BitOperations.RotateRight(val[tid], shift[tid]);
    }

    [GpuKernel]
    public static void Rotate64Kernel(ulong[] val, int[] shift, ulong[] rotLeftRes, ulong[] rotRightRes)
    {
        int tid = ThreadIndex.X;
        rotLeftRes[tid] = BitOperations.RotateLeft(val[tid], shift[tid]);
        rotRightRes[tid] = BitOperations.RotateRight(val[tid], shift[tid]);
    }
}
