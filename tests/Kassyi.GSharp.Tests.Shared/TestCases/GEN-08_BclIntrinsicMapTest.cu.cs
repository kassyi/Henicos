using System;
using Kassyi.GSharp.Core;
using System.Numerics;

namespace Kassyi.GSharp.Tests.TestCases;

public partial struct BclIntrinsicMapTestKernel : IKernel
{
    public void Process()
    {
        uint a = 10;
        ulong b = 20;
        nuint c = 30;
        
        int r1 = BitOperations.PopCount(a);
        int r2 = BitOperations.PopCount(b);
        int r3 = BitOperations.PopCount(c);

        int r4 = BitOperations.LeadingZeroCount(a);
        int r5 = BitOperations.LeadingZeroCount(b);
        int r6 = BitOperations.LeadingZeroCount(c);

        int r7 = BitOperations.TrailingZeroCount(a);
        int r8 = BitOperations.TrailingZeroCount(b);
        int r9 = BitOperations.TrailingZeroCount(c);

        int r10 = BitOperations.Log2(a);
        int r11 = BitOperations.Log2(b);
        int r12 = BitOperations.Log2(c);

        uint r13 = BitOperations.RotateLeft(a, 5);
        ulong r14 = BitOperations.RotateLeft(b, 5);
        nuint r15 = BitOperations.RotateLeft(c, 5);

        uint r16 = BitOperations.RotateRight(a, 5);
        ulong r17 = BitOperations.RotateRight(b, 5);
        nuint r18 = BitOperations.RotateRight(c, 5);
    }
}
