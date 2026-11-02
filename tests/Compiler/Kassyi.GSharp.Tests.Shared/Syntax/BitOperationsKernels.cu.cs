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
    public static void ReorderedNamedArgsKernel(
        int[] intTargets,
        long[] longTargets,
        uint[] u32Values,
        ulong[] u64Values,
        int[] intResults,
        long[] longResults,
        uint[] u32Results,
        ulong[] u64Results)
    {
        int tid = ThreadIndex.X;
        if (tid != 0)
        {
            return;
        }

        // Reordered named arguments on 3-arg and 2-arg Interlocked intrinsics (int and long with CastFirstArgumentTo)
        intResults[0] = System.Threading.Interlocked.CompareExchange(ref intTargets[0], comparand: 10, value: 99);
        intResults[1] = System.Threading.Interlocked.CompareExchange(value: 77, location1: ref intTargets[1], comparand: 20);
        intResults[2] = System.Threading.Interlocked.Add(value: 5, location1: ref intTargets[2]);
        intResults[3] = System.Threading.Interlocked.Exchange(value: 42, location1: ref intTargets[3]);

        longResults[0] = System.Threading.Interlocked.CompareExchange(comparand: 100L, value: 999L, location1: ref longTargets[0]);
        longResults[1] = System.Threading.Interlocked.Add(value: 50L, location1: ref longTargets[1]);
        longResults[2] = System.Threading.Interlocked.Exchange(value: 420L, location1: ref longTargets[2]);

        // Reordered named arguments on BitOperations intrinsics
        u32Results[0] = BitOperations.RotateLeft(offset: 4, value: u32Values[0]);
        u32Results[1] = BitOperations.RotateRight(offset: 4, value: u32Values[0]);
        u64Results[0] = BitOperations.RotateLeft(offset: 8, value: u64Values[0]);
        u64Results[1] = BitOperations.RotateRight(offset: 8, value: u64Values[0]);

        // Evaluation order with reordered named arguments: written left-to-right order is preserved
        int step = 4;
        u32Results[2] = BitOperations.RotateLeft(offset: NextOffset(ref step), value: (uint)step);
        int casStep = 10;
        intResults[4] = System.Threading.Interlocked.CompareExchange(
            ref intTargets[4],
            comparand: NextOffset(ref casStep),
            value: casStep * 10);
    }
}
