// ReSharper disable All
using System;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class GBI_30_34_BCL_01_03_GbiAndBclKernels
{
    // [GBI-30] ThreadIndex / BlockIndex / BlockDimension / GridDimension 3D Access
    [GpuKernel]
    public static void ThreadIndex3DKernel(
        int[] threadIndices,
        int[] blockIndices,
        int[] blockDimensions,
        int[] gridDimensions)
    {
        int tx = ThreadIndex.X;
        int ty = ThreadIndex.Y;
        int tz = ThreadIndex.Z;

        int bx = BlockIndex.X;
        int by = BlockIndex.Y;
        int bz = BlockIndex.Z;

        int bdx = BlockDimension.X;
        int bdy = BlockDimension.Y;
        int bdz = BlockDimension.Z;

        int gdx = GridDimension.X;
        int gdy = GridDimension.Y;
        int gdz = GridDimension.Z;

        // Calculate 1D index within block
        int localIdx = tx + ty * bdx + tz * (bdx * bdy);
        // Calculate 1D block index within grid
        int blockIdx1D = bx + by * gdx + bz * (gdx * gdy);
        // Total 1D index
        int threadsPerBlock = bdx * bdy * bdz;
        int globalIdx = blockIdx1D * threadsPerBlock + localIdx;

        int baseIdx = globalIdx * 3;

        threadIndices[baseIdx + 0] = tx;
        threadIndices[baseIdx + 1] = ty;
        threadIndices[baseIdx + 2] = tz;

        blockIndices[baseIdx + 0] = bx;
        blockIndices[baseIdx + 1] = by;
        blockIndices[baseIdx + 2] = bz;

        blockDimensions[baseIdx + 0] = bdx;
        blockDimensions[baseIdx + 1] = bdy;
        blockDimensions[baseIdx + 2] = bdz;

        gridDimensions[baseIdx + 0] = gdx;
        gridDimensions[baseIdx + 1] = gdy;
        gridDimensions[baseIdx + 2] = gdz;
    }

    // [GBI-31] Shared memory access and SyncThreads across warps
    [GpuKernel]
    public static void SharedMemorySyncKernel(int[] input, int[] output, int length)
    {
        var sharedMem = new SharedMemory<int>(128);
        int tid = ThreadIndex.X;

        if (tid < length)
        {
            sharedMem[tid] = input[tid];
        }

        Gpu.SyncThreads();

        if (tid < length)
        {
            // Read from neighbor across warp boundary
            int targetIdx = (tid + 32) % length;
            output[tid] = sharedMem[targetIdx];
        }
    }

    // [GBI-32] High contention AtomicAdd stress test
    [GpuKernel]
    public static void AtomicAddStressKernel(int[] globalSum)
    {
        Gpu.AtomicAdd(ref globalSum[0], 1);
    }

    // [GBI-33] Multi-digit carry addition
    [GpuKernel]
    public static void MultiDigitAddKernel(uint[] a, uint[] b, uint[] result, uint[] carries)
    {
        int tid = ThreadIndex.X;
        ulong carry = 0;
        result[tid] = Gpu.AddWithCarry(a[tid], b[tid], ref carry);
        carries[tid] = (uint)carry;
    }

    // [GBI-34] Inline PTX execution
    [GpuKernel]
    public static void InlinePtxExecutionKernel(uint[] data)
    {
        int tid = ThreadIndex.X;
        if (tid == 0)
        {
            Gpu.InlinePtx("data[0] = 7777u;");
        }
    }

    // [BCL-01 & BCL-02] BitOperations (PopCount, Log2, LeadingZeroCount, TrailingZeroCount, RotateLeft, RotateRight)
    [GpuKernel]
    public static void BitOperationsTestKernel(uint[] u32Inputs, uint[] u32Results)
    {
        int tid = ThreadIndex.X;
        uint val = u32Inputs[tid];

        int baseIdx = tid * 6;
        u32Results[baseIdx + 0] = (uint)BitOperations.PopCount(val);
        u32Results[baseIdx + 1] = (uint)BitOperations.Log2(val);
        u32Results[baseIdx + 2] = (uint)BitOperations.LeadingZeroCount(val);
        u32Results[baseIdx + 3] = (uint)BitOperations.TrailingZeroCount(val);
        u32Results[baseIdx + 4] = BitOperations.RotateLeft(val, 5);
        u32Results[baseIdx + 5] = BitOperations.RotateRight(val, 3);
    }

    // [BCL-03] System.Math and System.MathF operations
    [GpuKernel]
    public static void MathFunctionsKernel(double[] dInputs, double[] dResults, float[] fInputs, float[] fResults)
    {
        int tid = ThreadIndex.X;

        double dVal = dInputs[tid];
        int dBase = tid * 8;
        dResults[dBase + 0] = Math.Abs(dVal);
        dResults[dBase + 1] = Math.Sqrt(Math.Abs(dVal));
        dResults[dBase + 2] = Math.Sin(dVal);
        dResults[dBase + 3] = Math.Cos(dVal);
        dResults[dBase + 4] = Math.Max(dVal, 10.0);
        dResults[dBase + 5] = Math.Min(dVal, -10.0);
        dResults[dBase + 6] = Math.Floor(dVal);
        dResults[dBase + 7] = Math.Ceiling(dVal);

        float fVal = fInputs[tid];
        int fBase = tid * 8;
        fResults[fBase + 0] = MathF.Abs(fVal);
        fResults[fBase + 1] = MathF.Sqrt(MathF.Abs(fVal));
        fResults[fBase + 2] = MathF.Sin(fVal);
        fResults[fBase + 3] = MathF.Cos(fVal);
        fResults[fBase + 4] = MathF.Max(fVal, 10.0f);
        fResults[fBase + 5] = MathF.Min(fVal, -10.0f);
        fResults[fBase + 6] = MathF.Floor(fVal);
        fResults[fBase + 7] = MathF.Ceiling(fVal);
    }
}
