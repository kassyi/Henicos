namespace Kassyi.GSharp.Stdlib.GpuMath.Tests;

public static class ReferenceNTT
{
    public const ulong P1 = 4253024257;
    public const ulong P2 = 4293918721;
    public const ulong G1 = 5;
    public const ulong G2 = 19;

    public static ulong CpuModPow(ulong baseVal, ulong exp, ulong p)
    {
        ulong res = 1;
        baseVal %= p;
        while (exp > 0)
        {
            if ((exp & 1) != 0)
            {
                res = (res * baseVal) % p;
            }

            baseVal = (baseVal * baseVal) % p;
            exp >>= 1;
        }

        return res;
    }

    public static uint[] GenerateTwiddle(int length, ulong p, ulong g, bool inverse)
    {
        var w = CpuModPow(g, (p - 1) / (uint)length, p);
        if (inverse)
        {
            w = CpuModPow(w, p - 2, p);
        }

        var twiddle = new uint[length];
        ulong current = 1;
        for (int i = 0; i < length; i++)
        {
            twiddle[i] = (uint)current;
            current = (current * w) % p;
        }

        return twiddle;
    }

    public static uint[] Unpack16(uint[] input, int transformLength, int numWords)
    {
        var output = new uint[transformLength];
        for (int i = 0; i < transformLength; i++)
        {
            if (i < numWords * 2)
            {
                var wordIdx = i / 2;
                var bitOffset = (i % 2) * 16;
                output[i] = (input[wordIdx] >> bitOffset) & 0xFFFF;
            }
            else
            {
                output[i] = 0;
            }
        }

        return output;
    }

    public static uint[] NTT(uint[] input, uint[] twiddle, ulong p)
    {
        int n = input.Length;
        int log2N = ComputeLog2(n);
        var output = new uint[n];

        for (int i = 0; i < n; i++)
        {
            output[ReverseBits(i, log2N)] = input[i];
        }

        for (int s = 0; s < log2N; s++)
        {
            ApplyButterflyStageInPlace(output, twiddle, p, n, 2 << s);
        }

        return output;
    }

    public static uint[] GpuNTTEmulator(uint[] input, uint[] twiddle, ulong p)
    {
        int n = input.Length;
        int log2L = ComputeLog2(n);
        var row = new uint[n];

        for (int tid = 0; tid < n / 2; tid++)
        {
            row[ReverseBits(tid, log2L)] = input[tid];
            row[ReverseBits(tid + n / 2, log2L)] = input[tid + n / 2];
        }

        for (int s = 0; s < log2L; s++)
        {
            var nextRow = (uint[])row.Clone();
            int len = 2 << s;
            int halfLen = len / 2;

            for (int tid = 0; tid < n / 2; tid++)
            {
                int blockIdx = tid / halfLen;
                int idx = tid % halfLen;
                int pos = blockIdx * len + idx;

                ulong w = twiddle[idx * (n / len)];
                uint u = row[pos];
                ulong v = (row[pos + halfLen] * w) % p;

                nextRow[pos] = (uint)((u + v) % p);
                nextRow[pos + halfLen] = (uint)((u + p - (v % p)) % p);
            }

            row = nextRow;
        }

        return (uint[])row.Clone();
    }

    public static uint[] Pointwise(uint[] a, uint[] b, ulong p)
    {
        var output = new uint[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            output[i] = (uint)(((ulong)a[i] * b[i]) % p);
        }

        return output;
    }

    public static uint[] CRT(uint[] res1, uint[] res2, int outNumWords)
    {
        int n = res1.Length;
        var output = new uint[outNumWords];

        ulong invN1 = CpuModPow((ulong)n, P1 - 2, P1);
        ulong invN2 = CpuModPow((ulong)n, P2 - 2, P2);

        for (int k = 0; k < n; k++)
        {
            ulong v1 = ((ulong)res1[k] * invN1) % P1;
            ulong v2 = ((ulong)res2[k] * invN2) % P2;

            ulong diff = v2 >= v1 ? v2 - v1 : v2 + P2 - v1;
            const ulong Prime1InverseModPrime2 = 105UL;
            ulong h = (diff * Prime1InverseModPrime2) % P2;
            ulong val = v1 + P1 * h;

            if (val > 0)
            {
                AccumulateCrtTerm(output, k, val, outNumWords);
            }
        }

        return output;
    }

    private static int ComputeLog2(int length)
    {
        int log2 = 0;
        while ((1 << log2) < length)
        {
            log2++;
        }

        return log2;
    }

    private static int ReverseBits(int value, int bitCount)
    {
        int reversed = 0;
        for (int i = 0; i < bitCount; i++)
        {
            reversed = (reversed << 1) | ((value >> i) & 1);
        }

        return reversed;
    }

    private static void ApplyButterflyStageInPlace(uint[] data, uint[] twiddle, ulong p, int n, int len)
    {
        int halfLen = len / 2;
        int stride = n / len;
        for (int block = 0; block < stride; block++)
        {
            for (int idx = 0; idx < halfLen; idx++)
            {
                int pos = block * len + idx;
                ulong w = twiddle[idx * stride];
                ulong u = data[pos];
                ulong v = (data[pos + halfLen] * w) % p;

                data[pos] = (uint)((u + v) % p);
                data[pos + halfLen] = (uint)((u + p - v) % p);
            }
        }
    }

    private static void AccumulateCrtTerm(uint[] output, int k, ulong val, int outNumWords)
    {
        int wordIdx = k / 2;
        int bitOffset = (k % 2) * 16;

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

        if (low32 != 0) AtomicAddWithCarry(output, wordIdx, low32, outNumWords);
        if (mid32 != 0) AtomicAddWithCarry(output, wordIdx + 1, mid32, outNumWords);
        if (high32 != 0) AtomicAddWithCarry(output, wordIdx + 2, high32, outNumWords);
    }

    private static void AtomicAddWithCarry(uint[] address, int wordIdx, uint val, int maxWords)
    {
        uint carry = val;
        int idx = wordIdx;
        while (carry > 0 && idx < maxWords)
        {
            ulong sum = (ulong)address[idx] + carry;
            address[idx] = (uint)(sum & 0xFFFFFFFF);
            carry = (uint)(sum >> 32);
            idx++;
        }
    }
}
