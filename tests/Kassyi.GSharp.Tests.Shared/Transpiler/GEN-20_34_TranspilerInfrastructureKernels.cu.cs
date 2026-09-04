// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Transpiler;

public static class GEN_20_34_TranspilerInfrastructureKernels
{
    // GEN-20: Mutually calling device functions (forward declarations test)
    [GpuDeviceFunction]
    public static int MutualFuncA(int count, int val)
    {
        if (count <= 0)
        {
            return val;
        }
        return MutualFuncB(count - 1, val + 10);
    }

    [GpuDeviceFunction]
    public static int MutualFuncB(int count, int val)
    {
        if (count <= 0)
        {
            return val;
        }
        return MutualFuncA(count - 1, val + 20);
    }

    // GEN-20: Function overloading deduplication & forward declaration test
    [GpuDeviceFunction]
    public static int MultiplyAdd(int a, int b)
    {
        return a * b;
    }

    [GpuDeviceFunction]
    public static int MultiplyAdd(int a, int b, int c)
    {
        return a * b + c;
    }

    // GEN-20 Kernel: Mutual Call Test
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    [GpuKernel]
    public static void MutualCallKernel(int[] countArray, int[] valArray, int[] resultArray)
    {
        int tid = ThreadIndex.X;
        resultArray[tid] = MutualFuncA(countArray[tid], valArray[tid]);
    }

    // GEN-20 Kernel: Overload Test
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    [GpuKernel]
    public static void OverloadTestKernel(int[] inputA, int[] inputB, int[] inputC, int[] output2Arg, int[] output3Arg)
    {
        int tid = ThreadIndex.X;
        output2Arg[tid] = MultiplyAdd(inputA[tid], inputB[tid]);
        output3Arg[tid] = MultiplyAdd(inputA[tid], inputB[tid], inputC[tid]);
    }

    // GEN-34 Kernel: Synchronized Array Transfer Test (Empty / Huge Array)
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    [GpuKernel]
    public static void ArrayTransferKernel(int[] data, int length)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid < length)
        {
            data[tid] = data[tid] + 42;
        }
    }
}
