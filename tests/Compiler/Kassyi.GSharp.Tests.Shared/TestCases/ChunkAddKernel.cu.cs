// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum
{
    public static partial class GpuBigIntKernels
    {
        [GpuKernel]
        public static void ChunkAddKernel(uint[] dA, uint[] dB, uint[] dResult, long[] carryBuffer, int numWords, int chunkSize)
        {
            int chunkIdx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            int start = chunkIdx * chunkSize;
            if (start >= numWords) return;
            
            int end = start + chunkSize;
            if (end > numWords) end = numWords;
            
            ulong localCarry = 0;
            for (int i = start; i < end; i++)
            {
                ulong current = (ulong)dA[i] + (ulong)dB[i] + localCarry;
                dResult[i] = (uint)(current & 0xFFFFFFFF);
                localCarry = current >> 32;
            }
            carryBuffer[chunkIdx + 1] = (long)localCarry;
        }
    }
}
