// ReSharper disable All
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class NumericsAndHwMathKernels
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

    [GpuKernel]
    public static void PtxIntrinsicsExpansionKernel(
        uint[] u32Out,
        ulong[] u64Out,
        int[] i32Out)
    {
        uint u32Val = 0x12345678u;
        ulong u64Val = 0x0123456789ABCDEFul;

        u32Out[0] = Gpu.BitFieldExtract(u32Val, 4, 8);
        u64Out[0] = Gpu.BitFieldExtract(u64Val, 8, 16);

        u32Out[1] = Gpu.BitFieldInsert(0xFFu, u32Val, 4, 8);
        u64Out[1] = Gpu.BitFieldInsert(0xFFFFul, u64Val, 8, 16);

        u32Out[2] = Gpu.Lop3(u32Val, 0x0F0F0F0Fu, 0xAAAAAAAAu, 0x54);

        u32Out[3] = Gpu.CountTrailingZeros(u32Val);
        u64Out[2] = Gpu.CountTrailingZeros(u64Val);

        u64Out[3] = Gpu.BitReverse64(u64Val);

        i32Out[0] = Gpu.DotProduct4(0x01020304, 0x05060708, 10);
        u32Out[4] = Gpu.DotProduct4(0x01020304u, 0x05060708u, 10u);

        u32Out[5] = Gpu.Sad4(0x10203040u, 0x01020304u, 5u);

        u32Out[6] = Gpu.MatchAnySync(0xFFFFFFFFu, u32Val);

        int matchPred = 0;
        u32Out[7] = Gpu.MatchAllSync(0xFFFFFFFFu, u32Val, ref matchPred);

        Gpu.NanoSleep(10);
        Gpu.SyncWarp();

        Gpu.ReductionAdd(ref u32Out[8], 42u);
    }
}
