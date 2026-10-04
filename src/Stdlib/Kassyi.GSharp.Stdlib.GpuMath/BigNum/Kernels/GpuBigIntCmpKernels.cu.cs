// ReSharper disable All
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// ReSharper disable once NotAccessedVariable -- Variables are accessed within inline PTX assembly blocks
// ReSharper disable once RedundantAssignment -- Assignments are consumed by inline PTX assembly blocks
#pragma warning disable GSH0014 // Justification: Lambda capture limitations in source generation prevent passing context by reference.
[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static partial class GpuBigIntCmpKernels
{
    [GpuKernel]
    public static void IsZeroKernel(uint[] dA, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            uint notZero = dA[i] != 0 ? 1u : 0u;
            uint mask = Gpu.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                notZero |= Gpu.ShuffleDownSync(mask, notZero, offset);
            }
            if ((ThreadIndex.X & 31) == 0 && notZero != 0)
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
            uint notOne = 0;
            if (i == 0)
            {
                notOne = dA[0] != 1 ? 1u : 0u;
            }
            else
            {
                notOne = dA[i] != 0 ? 1u : 0u;
            }

            uint mask = Gpu.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                notOne |= Gpu.ShuffleDownSync(mask, notOne, offset);
            }

            if ((ThreadIndex.X & 31) == 0 && notOne != 0)
            {
                Gpu.AtomicOr(ref dResult[0], 1);
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
            ulong val = 0;
            if (wa != wb)
            {
                // Pack index into upper 32 bits, and comparison result (2 if >, 1 if <) into lower 32 bits
                long diff = (long)wa - (long)wb;
                var signMask = (ulong)(diff >> 63);
                var resultBits = 2ul - (signMask & 1ul);
                val = ((ulong)i << 32) | resultBits;
            }

            uint mask = Gpu.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                uint high = Gpu.ShuffleDownSync(mask, (uint)(val >> 32), offset);
                uint low = Gpu.ShuffleDownSync(mask, (uint)val, offset);
                ulong otherVal = ((ulong)high << 32) | low;
                if (otherVal > val)
                {
                    val = otherVal;
                }
            }

            if ((ThreadIndex.X & 31) == 0 && val != 0)
            {
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
            uint totalBits = 0;
            if (w != 0)
            {
                uint bitLen = 0;
                while (w > 0)
                {
                    bitLen++;
                    w >>= 1;
                }
                totalBits = (uint)(i * 32) + bitLen;
            }

            uint mask = Gpu.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                uint otherVal = Gpu.ShuffleDownSync(mask, totalBits, offset);
                if (otherVal > totalBits)
                {
                    totalBits = otherVal;
                }
            }

            if ((ThreadIndex.X & 31) == 0 && totalBits != 0)
            {
                Gpu.AtomicMax(ref dResult[0], totalBits);
            }
        });
    }
}

