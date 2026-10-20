namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntCmpKernels
{
    [GpuKernel]
    public static void IsZeroKernel(uint[] dA, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            uint notZero = dA[i] != 0 ? 1u : 0u;
            uint mask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                notZero |= Warp.ShuffleDownSync(mask, notZero, offset);
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

            uint mask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                notOne |= Warp.ShuffleDownSync(mask, notOne, offset);
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
                long diff = wa - (long)wb;
                var signMask = (ulong)(diff >> 63);
                var resultBits = 2ul - (signMask & 1ul);
                val = ((ulong)i << 32) | resultBits;
            }

            uint mask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                uint high = Warp.ShuffleDownSync(mask, (uint)(val >> 32), offset);
                uint low = Warp.ShuffleDownSync(mask, (uint)val, offset);
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

            uint mask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                uint otherVal = Warp.ShuffleDownSync(mask, totalBits, offset);
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
    [GpuKernel]
    public static void VectorCompareScalarKernel(uint[] dA, uint[] s, uint[] dResult, int numWords, int opType)
    {
        DeviceVectorCompareScalarParallel(dA, s, dResult, numWords, opType);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorCompareScalarParallel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] s,
        uint[] result,
        int numWords,
        int opType)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int batchIdx = BlockIndex.X;
        int elementOffset = batchIdx * numWords;

        SharedMemory<ulong> warpMax = new SharedMemory<ulong>(32);

        ulong myVal = 0;
        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;
            uint wa = valid ? a[elementOffset + idx] : 0u;
            uint ws = valid ? s[idx] : 0u;

            if (valid && wa != ws)
            {
                long diff = wa - (long)ws;
                ulong signMask = (ulong)(diff >> 63);
                ulong resultBits = 2ul - (signMask & 1ul);
                myVal = ((ulong)idx << 32) | resultBits;
            }
        }

        uint mask = Warp.ActiveMask();
        for (uint offset = 16; offset > 0; offset /= 2)
        {
            uint high = Warp.ShuffleDownSync(mask, (uint)(myVal >> 32), offset);
            uint low = Warp.ShuffleDownSync(mask, (uint)myVal, offset);
            ulong otherVal = ((ulong)high << 32) | low;
            if (otherVal > myVal)
            {
                myVal = otherVal;
            }
        }

        int laneId = tid % 32;
        int warpId = tid / 32;
        if (laneId == 0)
        {
            warpMax[warpId] = myVal;
        }

        Gpu.SyncThreads();

        if (warpId == 0)
        {
            int numWarps = (bDim + 31) / 32;
            ulong val = laneId < numWarps ? warpMax[laneId] : 0ul;

            uint activeMask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                uint high = Warp.ShuffleDownSync(activeMask, (uint)(val >> 32), offset);
                uint low = Warp.ShuffleDownSync(activeMask, (uint)val, offset);
                ulong otherVal = ((ulong)high << 32) | low;
                if (otherVal > val)
                {
                    val = otherVal;
                }
            }

            if (laneId == 0)
            {
                warpMax[0] = val;
            }
        }
        Gpu.SyncThreads();

        if (tid == 0)
        {
            ulong finalVal = warpMax[0];
            uint cmpRes = (uint)(finalVal & 0xFFFFFFFFul); // 0 = EQ, 1 = LT, 2 = GT
            
            bool isTrue = false;
            switch (opType)
            {
                case 0: isTrue = (cmpRes == 0); break; // EQ
                case 1: isTrue = (cmpRes != 0); break; // NEQ
                case 2: isTrue = (cmpRes == 2); break; // GT
                case 3: isTrue = (cmpRes == 2 || cmpRes == 0); break; // GTE
                case 4: isTrue = (cmpRes == 1); break; // LT
                case 5: isTrue = (cmpRes == 1 || cmpRes == 0); break; // LTE
            }

            result[batchIdx] = isTrue ? 1u : 0u;
        }
    }

    [GpuKernel]
    public static void VectorCompareVectorKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords, int opType)
    {
        DeviceVectorCompareVectorParallel(dA, dB, dResult, numWords, opType);
    }

    // NOTE: The core logic of DeviceVectorCompareVectorParallel is nearly 99% identical to DeviceVectorCompareScalarParallel.
    // While this could theoretically be abstracted using a generic Functor pattern (e.g., passing a struct implementing IFetchOp),
    // we intentionally duplicate the code here. This is because these are critical, low-level math kernels heavily relying on 
    // Warp-level synchronization primitives (Warp.ShuffleDownSync) and specific register allocation constraints.
    // Relying on the C++ compiler (NVCC) to consistently and safely inline struct-based interface calls inside complex
    // loop bounds without register spilling or performance degradation is risky. Thus, manual duplication (boilerplating) 
    // is preferred for guaranteed zero-overhead performance and safety in the GSharp transpiler context.
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorCompareVectorParallel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        int numWords,
        int opType)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int batchIdx = BlockIndex.X;
        int elementOffset = batchIdx * numWords;

        SharedMemory<ulong> warpMax = new SharedMemory<ulong>(32);

        ulong myVal = 0;
        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;
            uint wa = valid ? a[elementOffset + idx] : 0u;
            uint wb = valid ? b[elementOffset + idx] : 0u;

            if (valid && wa != wb)
            {
                long diff = wa - (long)wb;
                ulong signMask = (ulong)(diff >> 63);
                ulong resultBits = 2ul - (signMask & 1ul);
                myVal = ((ulong)idx << 32) | resultBits;
            }
        }

        uint mask = Warp.ActiveMask();
        for (uint offset = 16; offset > 0; offset /= 2)
        {
            uint high = Warp.ShuffleDownSync(mask, (uint)(myVal >> 32), offset);
            uint low = Warp.ShuffleDownSync(mask, (uint)myVal, offset);
            ulong otherVal = ((ulong)high << 32) | low;
            if (otherVal > myVal)
            {
                myVal = otherVal;
            }
        }

        int laneId = tid % 32;
        int warpId = tid / 32;
        if (laneId == 0)
        {
            warpMax[warpId] = myVal;
        }

        Gpu.SyncThreads();

        if (warpId == 0)
        {
            int numWarps = (bDim + 31) / 32;
            ulong val = laneId < numWarps ? warpMax[laneId] : 0ul;

            uint activeMask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                uint high = Warp.ShuffleDownSync(activeMask, (uint)(val >> 32), offset);
                uint low = Warp.ShuffleDownSync(activeMask, (uint)val, offset);
                ulong otherVal = ((ulong)high << 32) | low;
                if (otherVal > val)
                {
                    val = otherVal;
                }
            }

            if (laneId == 0)
            {
                warpMax[0] = val;
            }
        }
        Gpu.SyncThreads();

        if (tid == 0)
        {
            ulong finalVal = warpMax[0];
            uint cmpRes = (uint)(finalVal & 0xFFFFFFFFul); // 0 = EQ, 1 = LT, 2 = GT
            
            bool isTrue = false;
            switch (opType)
            {
                case 0: isTrue = (cmpRes == 0); break; // EQ
                case 1: isTrue = (cmpRes != 0); break; // NEQ
                case 2: isTrue = (cmpRes == 2); break; // GT
                case 3: isTrue = (cmpRes == 2 || cmpRes == 0); break; // GTE
                case 4: isTrue = (cmpRes == 1); break; // LT
                case 5: isTrue = (cmpRes == 1 || cmpRes == 0); break; // LTE
            }

            result[batchIdx] = isTrue ? 1u : 0u;
        }
    }

    [GpuKernel]
    public static void VectorAndKernel(uint[] dA, uint[] dB, uint[] dResult, int length)
    {
        Gpu.GridStrideLoop(length, i =>
        {
            dResult[i] = (dA[i] != 0 && dB[i] != 0) ? 1u : 0u;
        });
    }

    [GpuKernel]
    public static void VectorOrKernel(uint[] dA, uint[] dB, uint[] dResult, int length)
    {
        Gpu.GridStrideLoop(length, i =>
        {
            dResult[i] = (dA[i] != 0 || dB[i] != 0) ? 1u : 0u;
        });
    }

    [GpuKernel]
    public static void VectorNotKernel(uint[] dA, uint[] dResult, int length)
    {
        Gpu.GridStrideLoop(length, i =>
        {
            dResult[i] = (dA[i] == 0) ? 1u : 0u;
        });
    }

    [GpuKernel]
    public static void VectorAnyTrueKernel(uint[] dA, uint[] dResult, int length)
    {
        Gpu.GridStrideLoop(length, i =>
        {
            uint isTrue = dA[i] != 0 ? 1u : 0u;
            uint mask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                isTrue |= Warp.ShuffleDownSync(mask, isTrue, offset);
            }
            if ((ThreadIndex.X & 31) == 0 && isTrue != 0)
            {
                Gpu.AtomicMax(ref dResult[0], 1);
            }
        });
    }

    [GpuKernel]
    public static void VectorAllTrueKernel(uint[] dA, uint[] dResult, int length)
    {
        Gpu.GridStrideLoop(length, i =>
        {
            uint isFalse = dA[i] == 0 ? 1u : 0u;
            uint mask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                isFalse |= Warp.ShuffleDownSync(mask, isFalse, offset);
            }
            if ((ThreadIndex.X & 31) == 0 && isFalse != 0)
            {
                Gpu.AtomicExch(ref dResult[0], 0); // Sets to 0 if any is false
            }
        });
    }
}
