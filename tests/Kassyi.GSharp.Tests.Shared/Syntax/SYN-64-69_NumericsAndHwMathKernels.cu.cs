// ReSharper disable All
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class SYN_64_69_NumericsAndHwMathKernels
{
    [GpuKernel]
    public static void BitOperationsTestKernel(uint[] u32Data, ulong[] u64Data)
    {
        uint u32Val = u32Data[0];
        u32Data[1] = (uint)BitOperations.LeadingZeroCount(u32Val);
        u32Data[2] = (uint)BitOperations.PopCount(u32Val);
        u32Data[3] = (uint)BitOperations.TrailingZeroCount(u32Val);
        u32Data[4] = (uint)BitOperations.Log2(u32Val);
        u32Data[5] = BitOperations.RotateLeft(u32Val, 5);
        u32Data[6] = BitOperations.RotateRight(u32Val, 3);

        ulong u64Val = u64Data[0];
        u64Data[1] = (ulong)BitOperations.LeadingZeroCount(u64Val);
        u64Data[2] = (ulong)BitOperations.PopCount(u64Val);
        u64Data[3] = (ulong)BitOperations.TrailingZeroCount(u64Val);
        u64Data[4] = (ulong)BitOperations.Log2(u64Val);
        u64Data[5] = BitOperations.RotateLeft(u64Val, 10);
        u64Data[6] = BitOperations.RotateRight(u64Val, 7);
    }
}
