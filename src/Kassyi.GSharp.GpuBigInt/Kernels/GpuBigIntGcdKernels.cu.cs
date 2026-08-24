using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.GpuBigInt.Kernels;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
public static class GpuBigIntGcdKernels
{
    [GpuDevice]
    public static int GetBitLength_Device(uint[] data, int numWords)
    {
        for (int i = numWords - 1; i >= 0; i--)
        {
            uint w = data[i];
            if (w != 0)
            {
                int bitLen = i * 32;
                while (w > 0)
                {
                    bitLen++;
                    w >>= 1;
                }
                return bitLen;
            }
        }
        return 0;
    }

    [GpuKernel]
    public static void GetLehmerStateKernel(uint[] a, uint[] b, int numWords, uint[] resultHost)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            int bitsA = GetBitLength_Device(a, numWords);
            int bitsB = GetBitLength_Device(b, numWords);

            int shift = bitsA - 64;
            if (shift < 0)
            {
                shift = 0;
            }

            ulong x = 0;
            ulong y = 0;

            int wordShift = shift / 32;
            int bitShift = shift % 32;

            if (wordShift < numWords)
            {
                uint xLow = a[wordShift];
                uint xHigh = 0;
                if (wordShift + 1 < numWords)
                {
                    xHigh = a[wordShift + 1];
                }

                uint xTop = 0;
                if (wordShift + 2 < numWords)
                {
                    xTop = a[wordShift + 2];
                }

                ulong x64 = xLow | ((ulong)xHigh << 32);
                if (bitShift > 0)
                {
                    x = (x64 >> bitShift) | ((ulong)xTop << (64 - bitShift));
                }
                else
                {
                    x = x64;
                }
            }

            if (wordShift < numWords)
            {
                uint yLow = b[wordShift];
                uint yHigh = 0;
                if (wordShift + 1 < numWords)
                {
                    yHigh = b[wordShift + 1];
                }

                uint yTop = 0;
                if (wordShift + 2 < numWords)
                {
                    yTop = b[wordShift + 2];
                }

                ulong y64 = yLow | ((ulong)yHigh << 32);
                if (bitShift > 0)
                {
                    y = (y64 >> bitShift) | ((ulong)yTop << (64 - bitShift));
                }
                else
                {
                    y = y64;
                }
            }

            resultHost[0] = (uint)(x & 0xFFFFFFFF);
            resultHost[1] = (uint)(x >> 32);
            resultHost[2] = (uint)(y & 0xFFFFFFFF);
            resultHost[3] = (uint)(y >> 32);
            resultHost[4] = (uint)bitsA;
            resultHost[5] = (uint)bitsB;
        }
    }

    [GpuKernel]
    public static void ApplyLehmerMatrixKernel(uint[] a, uint[] b, uint[] newA, uint[] newB, int numWords, long u0, long v0, long u1, long v1, uint[] resultHost)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
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

            if (carryA < 0 || carryB < 0)
            {
                resultHost[0] = 0;
            }
            else
            {
                bool aGeB = false;
                bool aEqB = true;
                for (int i = numWords - 1; i >= 0; i--)
                {
                    if (newA[i] > newB[i]) { aGeB = true; aEqB = false; break; }
                    if (newA[i] < newB[i]) { aGeB = false; aEqB = false; break; }
                }

                if (aGeB || aEqB)
                {
                    resultHost[0] = 1;
                }
                else
                {
                    resultHost[0] = 0;
                }
            }
        }
    }
}
