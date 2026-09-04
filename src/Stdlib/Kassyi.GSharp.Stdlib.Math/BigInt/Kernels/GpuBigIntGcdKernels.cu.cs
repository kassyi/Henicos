// ReSharper disable All
namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntGcdKernels
{
    [GpuDeviceFunction]
    public static int GetBitLength_Device(uint[] data, int numWords)
    {
        for (int i = numWords - 1; i >= 0; i--)
        {
            var w = data[i];
            if (w != 0)
            {
                var bitLen = i * 32;
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

    [GpuDeviceFunction]
    public static bool IsZero_Device(uint[] data, int numWords)
    {
        for (int i = 0; i < numWords; i++)
        {
            if (data[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    [GpuDeviceFunction]
    public static int Compare_Device(uint[] a, uint[] b, int numWords)
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
    public static void Swap_Device(uint[] a, uint[] b, int numWords)
    {
        for (int i = 0; i < numWords; i++)
        {
            var t = a[i];
            a[i] = b[i];
            b[i] = t;
        }
    }

    [GpuDeviceFunction]
    public static void Sub_Device(uint[] a, uint[] b, int numWords)
    {
        long borrow = 0;
        for (int i = 0; i < numWords; i++)
        {
            var diff = (long)a[i] - b[i] + borrow;
            a[i] = (uint)(diff & 0xFFFFFFFF);
            borrow = diff >> 32;
        }
    }

    [GpuDeviceFunction]
    public static void Add_Device(uint[] a, uint[] b, int numWords)
    {
        long carry = 0;
        for (int i = 0; i < numWords; i++)
        {
            var sum = (long)a[i] + b[i] + carry;
            a[i] = (uint)(sum & 0xFFFFFFFF);
            carry = sum >> 32;
        }
    }

    [GpuDeviceFunction]
    public static void ShiftRight1_Device(uint[] a, int numWords)
    {
        uint carry = 0;
        for (int i = numWords - 1; i >= 0; i--)
        {
            var nextCarry = (a[i] & 1) << 31;
            a[i] = (a[i] >> 1) | carry;
            carry = nextCarry;
        }
    }

    [GpuKernel]
    public static void Kernel_Gcd(uint[] a, uint[] b, uint[] result, int numWords)
    {
        if (numWords <= 0)
        {
            return;
        }

        Gpu.SingleThread(() =>
        {
            if (IsZero_Device(a, numWords))
            {
                for (int i = 0; i < numWords; i++)
                {
                    result[i] = b[i];
                }

                return;
            }

            if (IsZero_Device(b, numWords))
            {
                for (int i = 0; i < numWords; i++)
                {
                    result[i] = a[i];
                }

                return;
            }

            int commonShift = 0;
            while ((a[0] & 1) == 0 && (b[0] & 1) == 0)
            {
                ShiftRight1_Device(a, numWords);
                ShiftRight1_Device(b, numWords);
                commonShift++;
            }

            while (!IsZero_Device(b, numWords))
            {
                while ((a[0] & 1) == 0 && !IsZero_Device(a, numWords))
                {
                    ShiftRight1_Device(a, numWords);
                }

                while ((b[0] & 1) == 0 && !IsZero_Device(b, numWords))
                {
                    ShiftRight1_Device(b, numWords);
                }

                if (Compare_Device(a, b, numWords) < 0)
                {
                    Swap_Device(a, b, numWords);
                }

                if (IsZero_Device(b, numWords))
                {
                    break;
                }

                var bitsA = GetBitLength_Device(a, numWords);
                var shift = bitsA - 64;
                if (shift < 0)
                {
                    shift = 0;
                }

                ulong x = 0;
                ulong y = 0;
                var wordShift = shift / 32;
                var bitShift = shift % 32;

                if (wordShift < numWords)
                {
                    var xLow = a[wordShift];
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

                    var x64 = xLow | ((ulong)xHigh << 32);
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
                    var yLow = b[wordShift];
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

                    var y64 = yLow | ((ulong)yHigh << 32);
                    if (bitShift > 0)
                    {
                        y = (y64 >> bitShift) | ((ulong)yTop << (64 - bitShift));
                    }
                    else
                    {
                        y = y64;
                    }
                }

                if (y == 0)
                {
                    Sub_Device(a, b, numWords);
                    ShiftRight1_Device(a, numWords);
                    continue;
                }

                long u0 = 1, v0 = 0;
                long u1 = 0, v1 = 1;
                var progress = false;

                while (y != 0)
                {
                    if (y == ulong.MaxValue || x == ulong.MaxValue)
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

                    var next_u = u0 - q * u1;
                    var next_v = v0 - q * v1;

                    // Stop if matrix entries grow beyond 32-bit to prevent overflow in Apply
                    if (next_u > 0x7FFFFFFF || next_u < -0x7FFFFFFF ||
                        next_v > 0x7FFFFFFF || next_v < -0x7FFFFFFF)
                    {
                        break;
                    }

                    x = y;
                    y = r;
                    u0 = u1;
                    v0 = v1;
                    u1 = next_u;
                    v1 = next_v;
                    progress = true;
                }

                if (!progress)
                {
                    Sub_Device(a, b, numWords);
                    ShiftRight1_Device(a, numWords);
                    continue;
                }


                long carryA2 = 0;
                long carryB2 = 0;
                for (int i = 0; i < numWords; i++)
                {
                    long valA = a[i];
                    long valB = b[i];

                    var nextA = valA * u0 + valB * v0 + carryA2;
                    var nextB = valA * u1 + valB * v1 + carryB2;

                    a[i] = (uint)(nextA & 0xFFFFFFFF);
                    carryA2 = nextA >> 32;

                    b[i] = (uint)(nextB & 0xFFFFFFFF);
                    carryB2 = nextB >> 32;
                }
            }

            for (int s = 0; s < commonShift; s++)
            {
                uint carry = 0;
                for (int i = 0; i < numWords; i++)
                {
                    var nextCarry = a[i] >> 31;
                    a[i] = (a[i] << 1) | carry;
                    carry = nextCarry;
                }
            }

            for (int i = 0; i < numWords; i++)
            {
                result[i] = a[i];
            }
        });
    }
}

