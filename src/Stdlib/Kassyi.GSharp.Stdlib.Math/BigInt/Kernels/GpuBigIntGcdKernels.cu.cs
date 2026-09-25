// ReSharper disable All
namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntGcdKernels
{
    [GpuKernel]
    public static void ApplyMatrix2x2Kernel(uint[] a, uint[] b, uint[] dstA, uint[] dstB, long u0, long v0, long u1, long v1, int numWords)
    {
        Gpu.SingleThread(() =>
        {
            long carryA = 0;
            long carryB = 0;
            for (int i = 0; i < numWords; i++)
            {
                long valA = a[i];
                long valB = b[i];

                var nextA = valA * u0 + valB * v0 + carryA;
                var nextB = valA * u1 + valB * v1 + carryB;

                dstA[i] = (uint)(nextA & 0xFFFFFFFF);
                carryA = nextA >> 32;

                dstB[i] = (uint)(nextB & 0xFFFFFFFF);
                carryB = nextB >> 32;
            }
        });
    }

    [GpuDeviceFunction]
    public static bool ComputeLehmerMatrix(ulong x, ulong y, out long u0, out long v0, out long u1, out long v1)
    {
        u0 = 1; v0 = 0;
        u1 = 0; v1 = 1;
        var progress = false;

        while (y != 0)
        {
            if (y + 1 == 0 || x == ~0ul)
            {
                break; // Overflow protection
            }

            var qMin = x / (y + 1);
            var qMax = (x + 1) / y;

            if (qMin != qMax)
            {
                break;
            }

            var q = (long)qMin;
            if (q == 0)
            {
                break;
            }

            var r = x - (ulong)q * y;

            var nextU = u0 - q * u1;
            var nextV = v0 - q * v1;

            if (nextU > 0x7FFFFFFF || nextU < -0x7FFFFFFF ||
                nextV > 0x7FFFFFFF || nextV < -0x7FFFFFFF)
            {
                break;
            }

            x = y;
            y = r;
            u0 = u1;
            v0 = v1;
            u1 = nextU;
            v1 = nextV;
            progress = true;
        }

        return progress;
    }

    [GpuDeviceFunction]
    private static int DeviceGetBitLength(uint[] data, int numWords)
    {
        for (int i = numWords - 1; i >= 0; i--)
        {
            if (data[i] == 0) continue;
            uint w = data[i];
            int b = 31;
            while ((w & (1u << b)) == 0) b--;
            return i * 32 + b + 1;
        }
        return 0;
    }

    [GpuDeviceFunction]
    private static ulong DeviceExtract64(uint[] data, int shift, int numWords)
    {
        int wordShift = shift / 32;
        int bitShift = shift % 32;
        if (wordShift >= numWords) return 0;

        ulong result = 0;
        
        uint w0 = data[wordShift];
        uint w1 = (wordShift + 1 < numWords) ? data[wordShift + 1] : 0u;
        uint w2 = (wordShift + 2 < numWords) ? data[wordShift + 2] : 0u;

        if (bitShift == 0)
        {
            result = w0 | ((ulong)w1 << 32);
        }
        else
        {
            uint low = (w0 >> bitShift) | (w1 << (32 - bitShift));
            uint high = (w1 >> bitShift) | (w2 << (32 - bitShift));
            result = low | ((ulong)high << 32);
        }
        return result;
    }

    [GpuDeviceFunction]
    private static int DeviceCompareTo(uint[] a, uint[] b, int numWords)
    {
        for (int i = numWords - 1; i >= 0; i--)
        {
            if (a[i] > b[i]) return 1;
            if (a[i] < b[i]) return -1;
        }
        return 0;
    }

    [GpuDeviceFunction]
    private static bool DeviceIsNegative(uint[] data, int numWords)
    {
        return (data[numWords - 1] & 0x80000000u) != 0;
    }

    [GpuKernel]
    public static void ExecuteLehmerGcdMacroKernel(
        uint[] a, uint[] b, uint[] newA, uint[] newB, int[] status, int numWords, int maxIterations)
    {
        Gpu.SingleThread(() =>
        {
            for (int iter = 0; iter < maxIterations; iter++)
            {
                int bitsA = DeviceGetBitLength(a, numWords);
                int bitsB = DeviceGetBitLength(b, numWords);
                
                if (bitsB == 0)
                {
                    status[0] = 2; // 2: Finished
                    break;
                }

                if (bitsA - bitsB > 64 || bitsA <= 64)
                {
                    status[0] = 1; // 1: NeedMod
                    break;
                }

                int shift = bitsA - 64;
                if (shift < 0) shift = 0;

                ulong x = DeviceExtract64(a, shift, numWords);
                ulong y = DeviceExtract64(b, shift, numWords);

                long u0, v0, u1, v1;
                bool progress = ComputeLehmerMatrix(x, y, out u0, out v0, out u1, out v1);

                if (!progress)
                {
                    status[0] = 1; // 1: NeedMod
                    break;
                }

                long carryA = 0;
                long carryB = 0;
                for (int i = 0; i < numWords; i++)
                {
                    long valA = a[i];
                    long valB = b[i];

                    long nextA = valA * u0 + valB * v0 + carryA;
                    long nextB = valA * u1 + valB * v1 + carryB;

                    newA[i] = (uint)(nextA & 0xFFFFFFFF);
                    carryA = nextA >> 32;

                    newB[i] = (uint)(nextB & 0xFFFFFFFF);
                    carryB = nextB >> 32;
                }

                if (DeviceIsNegative(newA, numWords) || DeviceIsNegative(newB, numWords) || DeviceCompareTo(newB, newA, numWords) > 0)
                {
                    status[0] = 1; // 1: NeedMod
                    break;
                }

                for (int i = 0; i < numWords; i++)
                {
                    a[i] = newA[i];
                    b[i] = newB[i];
                }
            }
        });
    }
}

