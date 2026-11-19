#nullable enable
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for signed <see cref="BitOperations.TrailingZeroCount(int)"/>, all
/// <see cref="BitOperations.IsPow2(int)"/> overloads, and unsigned <see cref="BitOperations.RoundUpToPowerOf2(uint)"/>.
/// </summary>
public static partial class BitOperationsPowerOfTwoDifferentialKernels
{
    public static readonly string[] TrailingZeroCountColumns =
    [
        "tzc_int32", "tzc_int64", "tzc_nint",
    ];

    public static readonly string[] IsPow2Columns =
    [
        "is_pow2_int32", "is_pow2_uint32", "is_pow2_int64", "is_pow2_nint", "is_pow2_uint64", "is_pow2_nuint",
    ];

    [GpuKernel]
    public static void TrailingZeroCountSignedKernel(int[] iVals, long[] lVals, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int baseIdx = t * 3;
        dst[baseIdx + 0] = BitOperations.TrailingZeroCount(iVals[t]);
        dst[baseIdx + 1] = BitOperations.TrailingZeroCount(lVals[t]);
        dst[baseIdx + 2] = BitOperations.TrailingZeroCount((nint)lVals[t]);
    }

    [GpuKernel]
    public static void IsPow2AllOverloadsKernel(
        int[] iVals,
        uint[] uVals,
        long[] lVals,
        ulong[] ulVals,
        bool[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int baseIdx = t * 6;
        dst[baseIdx + 0] = BitOperations.IsPow2(iVals[t]);
        dst[baseIdx + 1] = BitOperations.IsPow2(uVals[t]);
        dst[baseIdx + 2] = BitOperations.IsPow2(lVals[t]);
        dst[baseIdx + 3] = BitOperations.IsPow2((nint)lVals[t]);
        dst[baseIdx + 4] = BitOperations.IsPow2(ulVals[t]);
        dst[baseIdx + 5] = BitOperations.IsPow2((nuint)ulVals[t]);
    }

    [GpuKernel]
    public static void RoundUpToPowerOf2UnsignedKernel(
        uint[] uVals,
        ulong[] ulVals,
        uint[] uOut,
        ulong[] ulOut,
        ulong[] nuOut,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        uOut[t] = BitOperations.RoundUpToPowerOf2(uVals[t]);
        ulOut[t] = BitOperations.RoundUpToPowerOf2(ulVals[t]);
        nuOut[t] = BitOperations.RoundUpToPowerOf2((nuint)ulVals[t]);
    }
}
