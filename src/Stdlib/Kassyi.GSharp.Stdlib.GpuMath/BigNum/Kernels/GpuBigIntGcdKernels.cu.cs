// ReSharper disable All
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static partial class GpuBigIntGcdKernels
{
    public struct Matrix2x2ApplyContext
    {
        [GpuReadOnly] public uint[] A;
        [GpuReadOnly] public uint[] B;
        public uint[] DstA;
        public uint[] DstB;
        public long U0;
        public long V0;
        public long U1;
        public long V1;
        public int NumWords;
    }

    [GpuKernel]
    public static void ApplyMatrix2x2Kernel(Matrix2x2ApplyContext ctx)
    {
        Gpu.SingleThread(() =>
        {
            long carryA = 0;
            long carryB = 0;
            for (int i = 0; i < ctx.NumWords; i++)
            {
                long valA = ctx.A[i];
                long valB = ctx.B[i];

                var nextA = valA * ctx.U0 + valB * ctx.V0 + carryA;
                var nextB = valA * ctx.U1 + valB * ctx.V1 + carryB;

                ctx.DstA[i] = (uint)(nextA & 0xFFFFFFFF);
                carryA = nextA >> 32;

                ctx.DstB[i] = (uint)(nextB & 0xFFFFFFFF);
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
            if (data[i] == 0)
            {
                continue;
            }

            uint w = data[i];
            int b = 31;
            while ((w & (1u << b)) == 0)
            {
                b--;
            }

            return i * 32 + b + 1;
        }
        return 0;
    }

    [GpuDeviceFunction]
    private static ulong DeviceExtract64(uint[] data, int shift, int numWords)
    {
        int wordShift = shift / 32;
        int bitShift = shift % 32;
        if (wordShift >= numWords)
        {
            return 0;
        }

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
            if (a[i] > b[i])
            {
                return 1;
            }

            if (a[i] < b[i])
            {
                return -1;
            }
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
        uint[] a, uint[] b, uint[] newA, uint[] newB, int[] status, int numWords, int maxIterations = 1000)
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
                if (shift < 0)
                {
                    shift = 0;
                }

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

    [GpuDeviceFunction]
    private static void DeviceShiftRight1(uint[] data, int numWords)
    {
        uint carry = 0;
        for (int i = numWords - 1; i >= 0; i--)
        {
            uint w = data[i];
            data[i] = (w >> 1) | carry;
            carry = (w & 1) << 31;
        }
    }

    [GpuDeviceFunction]
    private static uint DeviceAdd(uint[] a, uint[] b, uint[] dst, int numWords)
    {
        ulong carry = 0;
        for (int i = 0; i < numWords; i++)
        {
            ulong sum = (ulong)a[i] + b[i] + carry;
            dst[i] = (uint)sum;
            carry = sum >> 32;
        }
        return (uint)carry;
    }

    [GpuDeviceFunction]
    private static void DeviceSub(uint[] a, uint[] b, uint[] dst, int numWords)
    {
        long borrow = 0;
        for (int i = 0; i < numWords; i++)
        {
            long diff = (long)a[i] - b[i] - borrow;
            dst[i] = (uint)diff;
            borrow = (diff < 0) ? 1 : 0;
        }
    }

    [GpuDeviceFunction]
    private static bool DeviceIsZero(uint[] data, int numWords)
    {
        for (int i = 0; i < numWords; i++)
        {
            if (data[i] != 0) return false;
        }
        return true;
    }

    [GpuDeviceFunction]
    private static bool DeviceIsOne(uint[] data, int numWords)
    {
        if (data[0] != 1) return false;
        for (int i = 1; i < numWords; i++)
        {
            if (data[i] != 0) return false;
        }
        return true;
    }

    [GpuKernel]
    public static void ExecuteExtendedBinaryGcdMacroKernel(
        uint[] m, uint[] u, uint[] v, uint[] x1, uint[] x2, int[] status, int numWords, int maxIterations)
    {
        Gpu.SingleThread(() =>
        {
            for (int iter = 0; iter < maxIterations; iter++)
            {
                if (DeviceIsZero(u, numWords) || DeviceIsZero(v, numWords))
                {
                    bool uIsOne = DeviceIsOne(u, numWords);
                    bool vIsOne = DeviceIsOne(v, numWords);
                    if (uIsOne || vIsOne)
                    {
                        if (vIsOne)
                        {
                            for (int i = 0; i < numWords; i++) x1[i] = x2[i];
                        }
                        status[0] = 1; // Success
                    }
                    else
                    {
                        status[0] = 2; // Failed (not coprime)
                    }
                    return;
                }

                if ((u[0] & 1) == 0) // u is even
                {
                    DeviceShiftRight1(u, numWords);
                    if ((x1[0] & 1) == 0)
                    {
                        DeviceShiftRight1(x1, numWords);
                    }
                    else
                    {
                        uint carry = DeviceAdd(x1, m, x1, numWords);
                        DeviceShiftRight1(x1, numWords);
                        if (carry != 0)
                        {
                            x1[numWords - 1] |= 0x80000000u;
                        }
                    }
                    continue;
                }

                if ((v[0] & 1) == 0) // v is even
                {
                    DeviceShiftRight1(v, numWords);
                    if ((x2[0] & 1) == 0)
                    {
                        DeviceShiftRight1(x2, numWords);
                    }
                    else
                    {
                        uint carry = DeviceAdd(x2, m, x2, numWords);
                        DeviceShiftRight1(x2, numWords);
                        if (carry != 0)
                        {
                            x2[numWords - 1] |= 0x80000000u;
                        }
                    }
                    continue;
                }

                // Both odd
                if (DeviceCompareTo(u, v, numWords) >= 0)
                {
                    DeviceSub(u, v, u, numWords); // u = u - v
                    if (DeviceCompareTo(x1, x2, numWords) >= 0)
                    {
                        DeviceSub(x1, x2, x1, numWords);
                    }
                    else
                    {
                        DeviceAdd(x1, m, x1, numWords);
                        DeviceSub(x1, x2, x1, numWords);
                    }
                }
                else
                {
                    DeviceSub(v, u, v, numWords); // v = v - u
                    if (DeviceCompareTo(x2, x1, numWords) >= 0)
                    {
                        DeviceSub(x2, x1, x2, numWords);
                    }
                    else
                    {
                        DeviceAdd(x2, m, x2, numWords);
                        DeviceSub(x2, x1, x2, numWords);
                    }
                }
            }
        });
    }
}

