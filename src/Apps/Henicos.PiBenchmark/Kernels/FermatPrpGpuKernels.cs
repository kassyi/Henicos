using System;
using Kassyi.GSharp.Core;

namespace Henicos.PiBenchmark.Kernels;

public static class FermatPrpGpuKernels
{
    // Constants for NTT
    public const uint NTT_P1 = 4253024257;
    public const ulong NTT_M1 = 4337323974UL;
    public const uint NTT_P2 = 4246732801;
    public const ulong NTT_M2 = 4343749639UL;

    // ---------------------------------------------------------
    // Phase 1: Squaring (Unpack)
    // ---------------------------------------------------------

    [GpuKernel]
    public static void Kernel_Unpack16(uint[] src, uint[] dst1, uint[] dst2, [JitConstant] int M,
        [JitConstant] int numWords)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= M)
        {
            return;
        }

        uint val = 0;
        int wordIdx = tid / 2;
        if (wordIdx < numWords)
        {
            uint w = src[wordIdx];
            if (tid % 2 == 0)
            {
                val = (w & 0xFFFF);
            }
            else
            {
                val = (w >> 16);
            }
        }

        dst1[tid] = val;
        dst2[tid] = val;
    }

    [GpuDeviceFunction]
    public static uint ModAdd(uint a, uint b, uint modulo)
    {
        uint res = a + b;
        if (res >= modulo || res < a)
        {
            res -= modulo;
        }

        return res;
    }

    [GpuDeviceFunction]
    public static uint ModSub(uint a, uint b, uint modulo)
    {
        uint res = a - b;
        if (a < b)
        {
            res += modulo;
        }

        return res;
    }

    [GpuDeviceFunction]
    public static uint ModMulBarrett(uint a, uint b, uint modulo, ulong mBarrett)
    {
        ulong prod = (ulong)a * b;
        ulong q = 0;
        Gpu.InlinePtx("asm(\"mul.hi.u64 %0, %1, %2;\" : \"=l\"(q) : \"l\"(prod), \"l\"(mBarrett));");
        uint res = (uint)(prod - q * modulo);
        if (res >= modulo)
        {
            res -= modulo;
        }

        return res;
    }

    [GpuDeviceFunction]
    public static uint ModPow(uint a, uint b, uint modulo, ulong mBarrett)
    {
        uint res = 1;
        a = ModMulBarrett(a, 1, modulo, mBarrett);
        while (b > 0)
        {
            if ((b & 1) != 0)
            {
                res = ModMulBarrett(res, a, modulo, mBarrett);
            }

            a = ModMulBarrett(a, a, modulo, mBarrett);
            b >>= 1;
        }

        return res;
    }

    [GpuDeviceFunction]
    public static void AtomicAddWithCarry(uint[] address, int wordIdx, uint val, int maxWords)
    {
        uint carry = val;
        int idx = wordIdx;
        while (carry > 0 && idx < maxWords)
        {
            uint old = Gpu.AtomicAdd(ref address[idx], carry);
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

    // ---------------------------------------------------------
    // Phase 2: NTT Global Cooley-Tukey
    // ---------------------------------------------------------

    [GpuKernel]
    public static void Kernel_NttGlobalPass(uint[] buf, uint[] twiddles, [JitConstant] int M,
        [JitConstant] int passIndex, [JitConstant] uint jitP, [JitConstant] ulong jitMBarrett)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int halfM = M / 2;
        if (tid >= halfM)
        {
            return;
        }

        // passIndex determines the size of the butterfly group
        // s = passIndex (1 to log2(M))
        int s = passIndex;
        int m = 1 << s;
        int half_m = m / 2;

        // Determine which butterfly group and index within the group
        int k = tid / half_m;
        int j = tid % half_m;

        int idx_even = k * m + j;
        int idx_odd = idx_even + half_m;

        // Twiddle factor index
        uint w = twiddles[j * (M / m)];
        uint t_val = ModMulBarrett(buf[idx_odd], w, jitP, jitMBarrett);
        uint u_val = buf[idx_even];

        buf[idx_even] = ModAdd(u_val, t_val, jitP);
        buf[idx_odd] = ModSub(u_val, t_val, jitP);
    }

    // ---------------------------------------------------------
    // Phase 3: Pack & Fermat Reduce
    // ---------------------------------------------------------

    [GpuKernel]
    public static void Kernel_BitReverse(uint[] src, uint[] dst, [JitConstant] int M, [JitConstant] int log2M)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid < M)
        {
            int rev = 0;
            for (int i = 0; i < log2M; i++)
            {
                rev = (rev << 1) | ((tid >> i) & 1);
            }

            dst[rev] = src[tid];
        }
    }

    [GpuKernel]
    public static void Kernel_PackAndFermatReduce(uint[] nttResult1, uint[] nttResult2, uint[] dst, [JitConstant] int M,
        [JitConstant] int outNumWords)
    {
        int k = BlockIndex.X * BlockDimension.X + ThreadIndex.X;

        if (k < M)
        {
            ulong p1 = NTT_P1;
            ulong p2 = NTT_P2;
            ulong p1_inv_p2 = 21; // invMod(p1, p2) = 21

            uint invN1 = ModPow((uint)M, NTT_P1 - 2, NTT_P1, NTT_M1);
            uint invN2 = ModPow((uint)M, NTT_P2 - 2, NTT_P2, NTT_M2);

            ulong v1 = ModMulBarrett(nttResult1[k], invN1, NTT_P1, NTT_M1);
            ulong v2 = ModMulBarrett(nttResult2[k], invN2, NTT_P2, NTT_M2);

            ulong diff = v2 >= v1 ? v2 - v1 : v2 + p2 - v1;
            ulong h = (diff * p1_inv_p2) % p2;
            ulong val = v1 + p1 * h;

            if (val <= 0)
            {
                return;
            }

            int wordIdx = k / 2;
            int bitOffset = (k % 2) * 16;

            uint low32 = 0;
            uint mid32 = 0;
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

            if (low32 > 0)
            {
                AtomicAddWithCarry(dst, wordIdx, low32, outNumWords);
            }

            if (mid32 > 0)
            {
                AtomicAddWithCarry(dst, wordIdx + 1, mid32, outNumWords);
            }

            if (high32 > 0)
            {
                AtomicAddWithCarry(dst, wordIdx + 2, high32, outNumWords);
            }
        }
    }
}
