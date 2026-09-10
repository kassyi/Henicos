// ReSharper disable All
namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// ReSharper disable once NotAccessedVariable -- Variables are accessed within inline PTX assembly blocks
// ReSharper disable once RedundantAssignment -- Assignments are consumed by inline PTX assembly blocks
#pragma warning disable GSH1002 // Justification: Lambda capture limitations in source generation prevent passing context by reference.
[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntCmpKernels
{
    [GpuKernel]
    public static void IsZeroKernel(uint[] dA, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            if (dA[i] != 0)
            {
                Gpu.AtomicOr(ref dResult[0], 1);
            }
        });
    }

    [GpuKernel]
    public static void IsOneKernel(uint[] dA, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            if (i == 0)
            {
                if (dA[0] != 1)
                {
                    Gpu.AtomicOr(ref dResult[0], 1);
                }
            }
            else
            {
                if (dA[i] != 0)
                {
                    Gpu.AtomicOr(ref dResult[0], 1);
                }
            }
        });
    }

    [GpuKernel]
    public static void CompareToKernel(uint[] dA, uint[] dB, ulong[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            var wa = dA[i];
            var wb = dB[i];
            if (wa != wb)
            {
                // Pack index into upper 32 bits, and comparison result (2 if >, 1 if <) into lower 32 bits
                long diff = (long)wa - (long)wb;
                var signMask = (ulong)(diff >> 63);
                var resultBits = 2ul - (signMask & 1ul);
                var val = ((ulong)i << 32) | resultBits;

                // Emits C++ atomicMax
                Gpu.AtomicMax(ref dResult[0], val);
            }
        });
    }

    [GpuKernel]
    public static void GetBitLengthKernel(uint[] dA, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            var w = dA[i];
            if (w != 0)
            {
                uint bitLen = 0;
                while (w > 0)
                {
                    bitLen++;
                    w >>= 1;
                }
                uint totalBits = (uint)(i * 32) + bitLen;
                Gpu.AtomicMax(ref dResult[0], totalBits);
            }
        });
    }
}

