// ReSharper disable All
#pragma warning disable IDE0051, IDE0022

namespace Kassyi.GSharp.Stdlib.Math.Kernels;

[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntScalarKernels
{
    [GpuKernel]
    public static void ScalarAddKernel(uint[] dA, uint s, uint[] dResult, int numWords)
    {
        Device_ScalarAdd(dA, s, dResult, numWords);
    }

    [GpuDeviceFunction]
    public static void Device_ScalarAdd(uint[] left, uint s, uint[] result, int numWords)
    {
        Gpu.SingleThread(() =>
        {
            ulong carry = s;
            for (int j = 0; j < numWords; j++)
            {
                var sum = left[j] + carry;
                result[j] = (uint)sum;
                carry = sum >> 32;
            }
        });
    }

    [GpuKernel]
    public static void ScalarSubKernel(uint[] dA, uint s, uint[] dResult, int numWords)
    {
        Device_ScalarSub(dA, s, dResult, numWords);
    }

    [GpuDeviceFunction]
    public static void Device_ScalarSub(uint[] left, uint s, uint[] result, int numWords)
    {
        Gpu.SingleThread(() =>
        {
            long borrow = s;
            for (int j = 0; j < numWords; j++)
            {
                var diff = left[j] - borrow;
                if (diff < 0)
                {
                    diff += 0x100000000L;
                    borrow = 1;
                }
                else
                {
                    borrow = 0;
                }

                result[j] = (uint)diff;
            }
        });
    }

    [GpuKernel]
    public static void ScalarMulKernel(uint[] dA, uint s, uint[] dResult, int numWords)
    {
        Device_ScalarMul(dA, s, dResult, numWords);
    }

    [GpuDeviceFunction]
    public static void Device_ScalarMul(uint[] left, uint s, uint[] result, int numWords)
    {
        Gpu.SingleThread(() =>
        {
            ulong carry = 0;
            for (int j = 0; j < numWords; j++)
            {
                var prod = (ulong)left[j] * s + carry;
                result[j] = (uint)(prod & 0xFFFFFFFFul);
                carry = prod >> 32;
            }
        });
    }

    [GpuKernel]
    public static void ChunkScalarMulKernel(uint[] dA, uint s, uint[] dResult, long[] dCarry, int numWords,
        int chunkSize)
    {
        Device_ChunkScalarMul(dA, s, dResult, dCarry, numWords, chunkSize);
    }

    [GpuDeviceFunction]
    public static void Device_ChunkScalarMul(uint[] left, uint s, uint[] result, long[] carryBuf, int numWords,
        int chunkSize)
    {
        var totalChunks = (numWords + chunkSize - 1) / chunkSize;
        Gpu.GridStrideLoop(totalChunks, chunkIdx =>
        {
            var start = chunkIdx * chunkSize;
            var end = start + chunkSize;
            if (end > numWords)
            {
                end = numWords;
            }

            ulong carry = 0;
            for (int i = start; i < end; i++)
            {
                var prod = (ulong)left[i] * s + carry;
                result[i] = (uint)(prod & 0xFFFFFFFFul);
                carry = prod >> 32;
            }

            carryBuf[chunkIdx + 1] = (long)carry;
        });
    }
}
