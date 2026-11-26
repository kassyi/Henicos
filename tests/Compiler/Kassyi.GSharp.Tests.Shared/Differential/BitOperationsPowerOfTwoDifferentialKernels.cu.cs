#nullable enable
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct PowerOfTwoCheckOperands
{
    [GpuReadOnly] public int[] Int32Values;
    [GpuReadOnly] public uint[] UInt32Values;
    [GpuReadOnly] public long[] Int64Values;
    [GpuReadOnly] public ulong[] UInt64Values;
}

public struct UnsignedPowerOfTwoOperands
{
    [GpuReadOnly] public uint[] UInt32Values;
    [GpuReadOnly] public ulong[] UInt64Values;
}

public struct UnsignedPowerOfTwoResults
{
    public uint[] UInt32Results;
    public ulong[] UInt64Results;
    public ulong[] NUIntResults;
}

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
    public static void IsPow2AllOverloadsKernel(PowerOfTwoCheckOperands operands, bool[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int baseIdx = t * 6;
        results[baseIdx + 0] = BitOperations.IsPow2(operands.Int32Values[t]);
        results[baseIdx + 1] = BitOperations.IsPow2(operands.UInt32Values[t]);
        results[baseIdx + 2] = BitOperations.IsPow2(operands.Int64Values[t]);
        results[baseIdx + 3] = BitOperations.IsPow2((nint)operands.Int64Values[t]);
        results[baseIdx + 4] = BitOperations.IsPow2(operands.UInt64Values[t]);
        results[baseIdx + 5] = BitOperations.IsPow2((nuint)operands.UInt64Values[t]);
    }

    [GpuKernel]
    public static void RoundUpToPowerOf2UnsignedKernel(
        UnsignedPowerOfTwoOperands operands,
        UnsignedPowerOfTwoResults results,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        results.UInt32Results[t] = BitOperations.RoundUpToPowerOf2(operands.UInt32Values[t]);
        results.UInt64Results[t] = BitOperations.RoundUpToPowerOf2(operands.UInt64Values[t]);
        results.NUIntResults[t] = BitOperations.RoundUpToPowerOf2((nuint)operands.UInt64Values[t]);
    }
}
