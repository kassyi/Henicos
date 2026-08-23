using Kassyi.GSharp.GpuBigInt.Contexts;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.GpuBigInt.Kernels;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
public static class GpuBigIntNttKernels
{
    // NTT Primes and primitive roots
    // We use two 32-bit primes for CRT to support convolution values up to 2^55
    // P1 = 63 * 2^26 + 1 (Proth prime)
    public const uint NttP1 = 4253024257; // 素数 (507 * 2^23 + 1, or something similar)
    public const uint NttG1 = 5; // 原始根

    // NTT素数 2
    public const uint NttP2 = 4293918721; // 素数 (4095 * 2^20 + 1)
    public const uint NttG2 = 19; // 原始根

    // Barrett Reduction Constants
    // M = floor(2^64 / P)
    // M1 = floor(2^64 / 4253024257) = 4337323974
    public const ulong NttM1 = 4337323974UL; // for P1

    // M2 = floor(2^64 / 4293918721) = 4296016127
    public const ulong NttM2 = 4296016127UL; // for P2

    [GpuDevice]
    public static uint ModAdd(uint a, uint b, uint p)
    {
        var res = a + b;
        if (res >= p || res < a)
        {
            res -= p;
        }

        return res;
    }

    [GpuDevice]
    public static uint ModSub(uint a, uint b, uint p)
    {
        var res = a - b;
        if (a < b)
        {
            res += p;
        }

        return res;
    }

    // ReSharper disable UselessBinaryOperation
    // ReSharper disable ConditionIsAlwaysTrueOrFalse
    [GpuDevice]
    public static uint ModMulBarrett(uint a, uint b, uint p, ulong m)
    {
        ulong prod = 0;
        Gpu.InlinePtx($"asm volatile(\"mul.wide.u32 %0, %1, %2;\" : \"=l\"({prod}) : \"r\"({a}), \"r\"({b}));");

        ulong q = 0;
        Gpu.InlinePtx($"asm volatile(\"mul.hi.u64 %0, %1, %2;\" : \"=l\"({q}) : \"l\"({prod}), \"l\"({m}));");

        ulong r = prod - q * p;
        while (r >= p)
        {
            r -= p;
        }

        return (uint)r;
    }
    // ReSharper restore ConditionIsAlwaysTrueOrFalse
    // ReSharper restore UselessBinaryOperation

    [GpuDevice]
    public static uint ModMul1(uint a, uint b) => ModMulBarrett(a, b, NttP1, NttM1);

    [GpuDevice]
    public static uint ModMul2(uint a, uint b) => ModMulBarrett(a, b, NttP2, NttM2);

    [GpuDevice]
    public static uint ModPow(uint baseVal, uint exp, uint p, ulong m)
    {
        uint res = 1;
        baseVal = baseVal % p;
        while (exp > 0)
        {
            if ((exp & 1) != 0)
            {
                res = ModMulBarrett(res, baseVal, p, m);
            }

            baseVal = ModMulBarrett(baseVal, baseVal, p, m);
            exp >>= 1;
        }

        return res;
    }

    [GpuDevice]
    public static void AtomicAddWithCarry(uint[] address, int wordIdx, uint val, int maxWords)
    {
        var carry = val;
        var idx = wordIdx;
        while (carry > 0 && idx < maxWords)
        {
            var old = Gpu.AtomicAdd(ref address[idx], carry);
            if (old + carry < old)
            {
                carry = 1;
                idx++;
            }
            else
            {
                break;
            }
        }
    }

    [GpuDevice]
    public static void AtomicSubWithBorrow(uint[] address, int wordIdx, uint val, int maxWords)
    {
        var borrow = val;
        var idx = wordIdx;
        while (borrow > 0 && idx < maxWords)
        {
            var old = Gpu.AtomicSub(ref address[idx], borrow);
            if (old < borrow)
            {
                borrow = 1;
                idx++;
            }
            else
            {
                break;
            }
        }
    }

    [GpuDevice]
    public static void Device_NTT_Shared_Row(ref NttKernelArgs args)
    {
        var dData = args.Buffer;
        var dTwiddle = args.Twiddle;
        var n = args.M;
        var l = args.CurrentL;
        var p = args.P;
        var m = args.Pm;
        var applyTwiddleBefore = args.ApplyTwiddleBefore;
        var applyTwiddleAfter = args.ApplyTwiddleAfter;
        var maxBlocks = args.MaxBlocks;
        var threadsNeeded = l / 2;
        var isActive = ThreadIndex.X < threadsNeeded && BlockIndex.X < maxBlocks;

        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");

        if (isActive)
        {
            var row = BlockIndex.X;
            var tid = ThreadIndex.X;
            var offset = row * l;

            int log2L = 0;
            while ((1 << log2L) < l)
            {
                log2L++;
            }

            int revTid1 = 0;
            int revTid2 = 0;
            for (int i = 0; i < log2L; i++)
            {
                revTid1 = (revTid1 << 1) | ((tid >> i) & 1);
                revTid2 = (revTid2 << 1) | (((tid + l / 2) >> i) & 1);
            }

            var val1 = dData[offset + tid];
            var val2 = dData[offset + tid + l / 2];

            if (applyTwiddleBefore)
            {
                var k1 = (ulong)row * (ulong)tid;
                k1 %= (ulong)n;
                val1 = ModMulBarrett(val1, dTwiddle[k1], p, m);

                var k2 = (ulong)row * (ulong)(tid + l / 2);
                k2 %= (ulong)n;
                val2 = ModMulBarrett(val2, dTwiddle[k2], p, m);
            }

            Gpu.InlinePtx($"s_row[{revTid1}] = {val1};");
            Gpu.InlinePtx($"s_row[{revTid2}] = {val2};");
        }
        Gpu.SyncThreads();

        int maxLog2L = 0;
        while ((1 << maxLog2L) < l)
        {
            maxLog2L++;
        }

        for (int s = 0; s < maxLog2L; s++)
        {
            if (isActive)
            {
                var tid = ThreadIndex.X;
                var len = 2 << s;
                var halfLen = len / 2;
                var blockIdx = tid / halfLen;
                var idx = tid % halfLen;
                var pos = blockIdx * len + idx;

                var twiddleIdx = idx * (n / len);
                var w = dTwiddle[twiddleIdx];

                uint u = 0;
                Gpu.InlinePtx($"{u} = s_row[{pos}];");

                uint vSrc = 0;
                Gpu.InlinePtx($"{vSrc} = s_row[{pos + halfLen}];");
                var v = ModMulBarrett(vSrc, w, p, m);

                var addRes = ModAdd(u, v, p);
                var subRes = ModSub(u, v, p);

                Gpu.InlinePtx($"s_row[{pos}] = {addRes};");
                Gpu.InlinePtx($"s_row[{pos + halfLen}] = {subRes};");
            }
            Gpu.SyncThreads();
        }

        if (isActive)
        {
            var row = BlockIndex.X;
            var tid = ThreadIndex.X;
            var offset = row * l;

            if (applyTwiddleAfter)
            {
                var k1 = (ulong)row * (ulong)tid;
                k1 %= (ulong)n;
                uint r1 = 0;
                Gpu.InlinePtx($"{r1} = s_row[{tid}];");
                r1 = ModMulBarrett(r1, dTwiddle[k1], p, m);
                Gpu.InlinePtx($"s_row[{tid}] = {r1};");

                var k2 = (ulong)row * (ulong)(tid + l / 2);
                k2 %= (ulong)n;
                uint r2 = 0;
                Gpu.InlinePtx($"{r2} = s_row[{tid + l / 2}];");
                r2 = ModMulBarrett(r2, dTwiddle[k2], p, m);
                Gpu.InlinePtx($"s_row[{tid + l / 2}] = {r2};");
            }

            uint out1 = 0;
            uint out2 = 0;
            Gpu.InlinePtx($"{out1} = s_row[{tid}];");
            Gpu.InlinePtx($"{out2} = s_row[{tid + l / 2}];");

            dData[offset + tid] = out1;
            dData[offset + tid + l / 2] = out2;
        }
    }

    [GpuDevice]
    public static void Device_NTT_PointwiseSquare(uint[] dData1, uint[] dData2, int n)
    {
        var tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        for (var i = tid; i < n; i += GridDimension.X * BlockDimension.X)
        {
            dData1[i] = ModMulBarrett(dData1[i], dData1[i], NttP1, NttM1);
            dData2[i] = ModMulBarrett(dData2[i], dData2[i], NttP2, NttM2);
        }
    }

    [GpuDevice]
    public static void Device_NTT_PointwiseMultiply(uint[] dData1A, uint[] dData2A, uint[] dData1B, uint[] dData2B, int n)
    {
        var tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        for (var i = tid; i < n; i += GridDimension.X * BlockDimension.X)
        {
            dData1A[i] = ModMulBarrett(dData1A[i], dData1B[i], NttP1, NttM1);
            dData2A[i] = ModMulBarrett(dData2A[i], dData2B[i], NttP2, NttM2);
        }
    }

    [GpuDevice]
    public static void Device_CRT_ParallelAtomic([GpuConst] uint[] dRes1, [GpuConst] uint[] dRes2, uint[] outUint,
        int n, int outNumWords)
    {
        var tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;

        var invN1 = ModPow((uint)n, NttP1 - 2, NttP1, NttM1);
        var invN2 = ModPow((uint)n, NttP2 - 2, NttP2, NttM2);

        for (var k = tid; k < n; k += GridDimension.X * BlockDimension.X)
        {
            const ulong P1 = NttP1;
            const ulong P2 = NttP2;

            ulong v1 = ModMulBarrett(dRes1[k], invN1, NttP1, NttM1);
            ulong v2 = ModMulBarrett(dRes2[k], invN2, NttP2, NttM2);

            ulong diff;
            if (v2 >= v1)
            {
                diff = v2 - v1;
            }
            else
            {
                diff = v2 + P2 - v1;
            }

            // CRT recombination
            // x = v1 + P1 * [(v2 - v1) * (P1^-1 mod P2) mod P2]
            // Prime1InverseModPrime2 = P1^-1 mod P2
            // P1 = 4253024257, P2 = 4293918721
            const ulong Prime1InverseModPrime2 = 105UL;
            var h = (diff * Prime1InverseModPrime2) % P2;
            var val = v1 + P1 * h;

            if (val <= 0)
            {
                continue;
            }

            var wordIdx = k / 2;
            var bitOffset = (k % 2) * 16;

            uint low32;
            uint mid32;
            uint high32 = 0;

            if (bitOffset == 0)
            {
                low32 = (uint)(val & 0xFFFFFFFF);
                mid32 = (uint)(val >> 32);
            }
            else
            {
                low32 = (uint)((val << 16) & 0xFFFFFFFF);
                mid32 = (uint)((val >> 16) & 0xFFFFFFFF);
                high32 = (uint)(val >> 48);
            }

            if (low32 != 0)
            {
                AtomicAddWithCarry(outUint, wordIdx, low32, outNumWords);
            }

            if (mid32 != 0)
            {
                AtomicAddWithCarry(outUint, wordIdx + 1, mid32, outNumWords);
            }

            if (high32 != 0)
            {
                AtomicAddWithCarry(outUint, wordIdx + 2, high32, outNumWords);
            }
        }
    }

    [GpuDevice]
    public static void Device_NTT_Internal(ref NttKernelArgs args)
    {
        var buf = args.Buffer;
        var tmp = args.TemporaryBuffer;
        var m = args.M;
        var n1 = args.N1;
        var n2 = args.N2;
        var isInverse = args.IsInverse;
        if (m <= 512)
        {
            args.CurrentL = m;
            args.ApplyTwiddleBefore = false;
            args.ApplyTwiddleAfter = false;
            args.MaxBlocks = 1;
            Device_NTT_Shared_Row(ref args);
            Gpu.SyncGrid();
        }
        else
        {
            if (isInverse)
            {
                args.CurrentL = n2;
                args.ApplyTwiddleBefore = false;
                args.ApplyTwiddleAfter = true;
                args.MaxBlocks = n1;
                Device_NTT_Shared_Row(ref args);
                Gpu.SyncGrid();

                GpuBigIntFermatKernels.Device_Transpose_32x32(buf, tmp, n2, n1);
                Gpu.SyncGrid();

                args.Buffer = tmp;
                args.CurrentL = n1;
                args.ApplyTwiddleBefore = false;
                args.ApplyTwiddleAfter = false;
                args.MaxBlocks = n2;
                Device_NTT_Shared_Row(ref args);
                args.Buffer = buf; // Restore buffer
                Gpu.SyncGrid();

                GpuBigIntFermatKernels.Device_Transpose_32x32(tmp, buf, n1, n2);
                Gpu.SyncGrid();
            }
            else
            {
                GpuBigIntFermatKernels.Device_Transpose_32x32(buf, tmp, n2, n1);
                Gpu.SyncGrid();

                args.Buffer = tmp;
                args.CurrentL = n1;
                args.ApplyTwiddleBefore = false;
                args.ApplyTwiddleAfter = true;
                args.MaxBlocks = n2;
                Device_NTT_Shared_Row(ref args);
                args.Buffer = buf; // Restore buffer
                Gpu.SyncGrid();

                GpuBigIntFermatKernels.Device_Transpose_32x32(tmp, buf, n1, n2);
                Gpu.SyncGrid();

                args.CurrentL = n2;
                args.ApplyTwiddleBefore = false;
                args.ApplyTwiddleAfter = false;
                args.MaxBlocks = n1;
                Device_NTT_Shared_Row(ref args);
                Gpu.SyncGrid();
            }
        }
    }

    [GpuDevice]
    public static void Device_ForwardNTT(ref NttKernelArgs args)
    {
        args.IsInverse = false;
        Device_NTT_Internal(ref args);
    }

    [GpuDevice]
    public static void Device_InverseNTT(ref NttKernelArgs args)
    {
        args.IsInverse = true;
        Device_NTT_Internal(ref args);
    }
}

