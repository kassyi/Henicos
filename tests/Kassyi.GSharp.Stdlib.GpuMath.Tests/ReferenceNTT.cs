namespace Kassyi.GSharp.Stdlib.GpuMath.Tests;

public class ReferenceNTT
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
        int log2N = (int)Math.Log(n, 2);
        var output = new uint[n];

        // Bit-reverse copy
        for (int i = 0; i < n; i++)
        {
            int rev = 0;
            for (int j = 0; j < log2N; j++)
            {
                rev = (rev << 1) | ((i >> j) & 1);
            }

            output[rev] = input[i];
        }

        // Butterfly
        for (int s = 0; s < log2N; s++)
        {
            int len = 2 << s;
            int halfLen = len / 2;
            for (int block = 0; block < n / len; block++)
            {
                for (int idx = 0; idx < halfLen; idx++)
                {
                    int pos = block * len + idx;
                    int twiddleIdx = idx * (n / len);
                    ulong w = twiddle[twiddleIdx];

                    ulong u = output[pos];
                    ulong vSrc = output[pos + halfLen];
                    ulong v = (vSrc * w) % p;

                    output[pos] = (uint)((u + v) % p);
                    output[pos + halfLen] = (uint)((u + p - v) % p);
                }
            }
        }

        return output;
    }

    public static uint[] GpuNTTEmulator(uint[] input, uint[] twiddle, ulong p)
    {
        int n = input.Length; // 64
        int l = n;
        var s_row = new uint[n];

        // Bit-reverse copy equivalent to GPU
        for (int tid = 0; tid < l / 2; tid++)
        {
            int log2L = 0;
            while ((1 << log2L) < l) log2L++;

            int revTid1 = 0;
            int revTid2 = 0;
            for (int i = 0; i < log2L; i++)
            {
                revTid1 = (revTid1 << 1) | ((tid >> i) & 1);
                revTid2 = (revTid2 << 1) | (((tid + l / 2) >> i) & 1);
            }

            s_row[revTid1] = input[tid];
            s_row[revTid2] = input[tid + l / 2];
        }

        // Butterfly
        int maxLog2L = 0;
        while ((1 << maxLog2L) < l) maxLog2L++;

        for (int s = 0; s < maxLog2L; s++)
        {
            // To simulate SyncThreads, we read from old state and write to new state
            var next_row = (uint[])s_row.Clone();

            for (int tid = 0; tid < l / 2; tid++)
            {
                int len = 2 << s;
                int halfLen = len / 2;
                int blockIdx = tid / halfLen;
                int idx = tid % halfLen;
                int pos = blockIdx * len + idx;

                int twiddleIdx = idx * (n / len);
                ulong w = twiddle[twiddleIdx];

                uint u = s_row[pos];
                uint vSrc = s_row[pos + halfLen];

                // ModMulBarrett equivalent
                ulong v = (vSrc * w) % p;

                // ModAdd / ModSub
                uint addRes = (uint)((u + v) % p);
                uint subRes = (uint)((u + p - (v % p)) % p);

                next_row[pos] = addRes;
                next_row[pos + halfLen] = subRes;
            }

            s_row = next_row;
        }

        var output = new uint[n];
        for (int tid = 0; tid < l / 2; tid++)
        {
            output[tid] = s_row[tid];
            output[tid + l / 2] = s_row[tid + l / 2];
        }

        return output;
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
                int wordIdx = k / 2;
                int bitOffset = (k % 2) * 16;

                uint low32, mid32, high32 = 0;
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
        }

        return output;
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
