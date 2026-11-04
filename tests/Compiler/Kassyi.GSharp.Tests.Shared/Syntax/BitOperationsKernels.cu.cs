// ReSharper disable All
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

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

    [GpuDeviceFunction]
    public static int NextOffset(ref int counter) => counter++;

    [GpuKernel]
    public static void InterlockedReorderedNamedArgsKernel(
        int[] targets,
        int[] results)
    {
        int tid = ThreadIndex.X;
        if (tid != 0)
        {
            return;
        }

        results[0] = System.Threading.Interlocked.CompareExchange(ref targets[0], comparand: 10, value: 99);
        results[1] = System.Threading.Interlocked.CompareExchange(value: 77, location1: ref targets[1], comparand: 20);
        results[2] = System.Threading.Interlocked.Add(value: 5, location1: ref targets[2]);
        results[3] = System.Threading.Interlocked.Exchange(value: 42, location1: ref targets[3]);
    }

    [GpuKernel]
    public static void InterlockedLongReorderedNamedArgsKernel(
        long[] targets,
        long[] results)
    {
        int tid = ThreadIndex.X;
        if (tid != 0)
        {
            return;
        }

        results[0] = System.Threading.Interlocked.CompareExchange(comparand: 100L, value: 999L, location1: ref targets[0]);
        results[1] = System.Threading.Interlocked.Add(value: 50L, location1: ref targets[1]);
        results[2] = System.Threading.Interlocked.Exchange(value: 420L, location1: ref targets[2]);
    }

    [GpuKernel]
    public static void RotationReorderedNamedArgsKernel(
        uint[] values,
        uint[] results)
    {
        int tid = ThreadIndex.X;
        if (tid != 0)
        {
            return;
        }

        results[0] = BitOperations.RotateLeft(offset: 4, value: values[0]);
        results[1] = BitOperations.RotateRight(offset: 4, value: values[0]);
    }

    [GpuKernel]
    public static void RotationLongReorderedNamedArgsKernel(
        ulong[] values,
        ulong[] results)
    {
        int tid = ThreadIndex.X;
        if (tid != 0)
        {
            return;
        }

        results[0] = BitOperations.RotateLeft(offset: 8, value: values[0]);
        results[1] = BitOperations.RotateRight(offset: 8, value: values[0]);
    }

    [GpuKernel]
    public static void ReorderedNamedArgsEvaluationOrderKernel(
        int[] targets,
        int[] intResults,
        uint[] u32Results)
    {
        int tid = ThreadIndex.X;
        if (tid != 0)
        {
            return;
        }

        int step = 4;
        u32Results[0] = BitOperations.RotateLeft(offset: NextOffset(ref step), value: (uint)step);
        int casStep = 10;
        intResults[0] = System.Threading.Interlocked.CompareExchange(
            ref targets[0],
            comparand: NextOffset(ref casStep),
            value: casStep * 10);
    }
}
