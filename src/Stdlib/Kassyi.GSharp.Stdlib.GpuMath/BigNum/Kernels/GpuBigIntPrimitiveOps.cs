// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

/// <summary>
/// Provides reusable zero-allocation GPU device primitives for low-level multi-limb arithmetic,
/// comparisons, and bit-level manipulations across custom GPU kernels.
/// </summary>
public static partial class GpuBigIntPrimitiveOps
{
    /// <summary>
    /// Computes the bit length of a multi-limb integer in GPU device memory.
    /// </summary>
    /// <param name="data">Pointer to the starting limb array in device memory.</param>
    /// <param name="numWords">The total number of 32-bit words in the multi-limb integer.</param>
    /// <returns>The total number of significant bits required to represent the integer.</returns>
    [GpuDeviceFunction]
    public static unsafe int DeviceGetBitLength(uint* data, int numWords)
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

    /// <summary>
    /// Extracts a 64-bit window from a multi-limb integer in GPU device memory at the specified bit shift.
    /// </summary>
    /// <param name="data">Pointer to the starting limb array in device memory.</param>
    /// <param name="shift">The bit offset from which to extract the 64-bit value.</param>
    /// <param name="numWords">The total number of 32-bit words in the multi-limb integer.</param>
    /// <returns>A 64-bit unsigned integer containing the bits starting at the specified shift.</returns>
    [GpuDeviceFunction]
    public static unsafe ulong DeviceExtract64(uint* data, int shift, int numWords)
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

    /// <summary>
    /// Compares two multi-limb integers in GPU device memory.
    /// </summary>
    /// <param name="a">Pointer to the left limb array in device memory.</param>
    /// <param name="b">Pointer to the right limb array in device memory.</param>
    /// <param name="numWords">The total number of 32-bit words to compare.</param>
    /// <returns>1 if a &gt; b, -1 if a &lt; b, or 0 if equal.</returns>
    [GpuDeviceFunction]
    public static unsafe int DeviceCompareTo(uint* a, uint* b, int numWords)
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

    /// <summary>
    /// Checks whether the most significant bit is set (negative representation in two's complement).
    /// </summary>
    /// <param name="data">Pointer to the starting limb array in device memory.</param>
    /// <param name="numWords">The total number of 32-bit words in the multi-limb integer.</param>
    /// <returns><see langword="true"/> if the integer represents a negative value in two's complement; otherwise, <see langword="false"/>.</returns>
    [GpuDeviceFunction]
    public static unsafe bool DeviceIsNegative(uint* data, int numWords)
    {
        return (data[numWords - 1] & 0x80000000u) != 0;
    }

    /// <summary>
    /// Right-shifts a multi-limb integer by 1 bit in-place in GPU device memory.
    /// </summary>
    /// <param name="data">Pointer to the starting limb array in device memory.</param>
    /// <param name="numWords">The total number of 32-bit words in the multi-limb integer.</param>
    [GpuDeviceFunction]
    public static unsafe void DeviceShiftRight1(uint* data, int numWords)
    {
        uint carry = 0;
        for (int i = numWords - 1; i >= 0; i--)
        {
            uint w = data[i];
            data[i] = (w >> 1) | carry;
            carry = (w & 1) << 31;
        }
    }

    /// <summary>
    /// Adds two multi-limb integers in GPU device memory and returns the final carry bit.
    /// </summary>
    /// <param name="a">Pointer to the first source limb array in device memory.</param>
    /// <param name="b">Pointer to the second source limb array in device memory.</param>
    /// <param name="dst">Pointer to the destination limb array in device memory.</param>
    /// <param name="numWords">The total number of 32-bit words to add.</param>
    /// <returns>The overflow carry bit (0 or 1) generated from adding the highest limbs.</returns>
    [GpuDeviceFunction]
    public static unsafe uint DeviceAdd(uint* a, uint* b, uint* dst, int numWords)
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

    /// <summary>
    /// Subtracts the right multi-limb integer from the left in GPU device memory.
    /// </summary>
    /// <param name="a">Pointer to the minuend limb array in device memory.</param>
    /// <param name="b">Pointer to the subtrahend limb array in device memory.</param>
    /// <param name="dst">Pointer to the destination limb array in device memory.</param>
    /// <param name="numWords">The total number of 32-bit words to subtract.</param>
    [GpuDeviceFunction]
    public static unsafe void DeviceSub(uint* a, uint* b, uint* dst, int numWords)
    {
        long borrow = 0;
        for (int i = 0; i < numWords; i++)
        {
            long diff = (long)a[i] - b[i] - borrow;
            dst[i] = (uint)diff;
            borrow = (diff < 0) ? 1 : 0;
        }
    }

    /// <summary>
    /// Determines whether all limbs of an integer in GPU device memory are zero.
    /// </summary>
    /// <param name="data">Pointer to the limb array in device memory.</param>
    /// <param name="numWords">The total number of 32-bit words to check.</param>
    /// <returns><see langword="true"/> if all limbs are zero; otherwise, <see langword="false"/>.</returns>
    [GpuDeviceFunction]
    public static unsafe bool DeviceIsZero(uint* data, int numWords)
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

    /// <summary>
    /// Determines whether an integer in GPU device memory equals one.
    /// </summary>
    /// <param name="data">Pointer to the limb array in device memory.</param>
    /// <param name="numWords">The total number of 32-bit words to check.</param>
    /// <returns><see langword="true"/> if the multi-limb integer evaluates to 1; otherwise, <see langword="false"/>.</returns>
    [GpuDeviceFunction]
    public static unsafe bool DeviceIsOne(uint* data, int numWords)
    {
        if (data[0] != 1)
        {
            return false;
        }

        for (int i = 1; i < numWords; i++)
        {
            if (data[i] != 0)
            {
                return false;
            }
        }

        return true;
    }
}
