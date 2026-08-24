using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Stdlib.Math.Algorithms;

/// <summary>
/// 3-Prime NTT (Number Theoretic Transform) and Garner's CRT combination for O(N log N) large integer multiplication.
/// CPU reference implementation designed for validation before GPU porting.
/// </summary>
public static class NttCpuReference
{
    // The three Solinas-like Proth primes chosen for 64-bit non-overflowing arithmetic
    public const uint P1 = 2130706433; // 2^24 * 127 + 1
    public const uint P2 = 1224736769; // 73 * 2^24 + 1
    public const uint P3 = 754974721; // 45 * 2^24 + 1

    public const uint G1 = 3;
    public const uint G2 = 3;
    public const uint G3 = 11;

    // ModPow for uint
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModPow(uint b, uint e, uint m)
    {
        ulong res = 1;
        ulong baseVal = b;
        while (e > 0)
        {
            if ((e & 1) == 1)
            {
                res = (res * baseVal) % m;
            }

            baseVal = (baseVal * baseVal) % m;
            e >>= 1;
        }

        return (uint)res;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModInverse(uint a, uint m) => ModPow(a, m - 2, m);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModAdd(uint a, uint b, uint m)
    {
        var res = a + b;
        if (res >= m)
        {
            res -= m;
        }

        return res;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModSub(uint a, uint b, uint m)
    {
        if (a < b)
        {
            return a + m - b;
        }

        return a - b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModMul(uint a, uint b, uint m) => (uint)(((ulong)a * b) % m);

    public static void Ntt(uint[] a, uint p, uint g, bool inverse)
    {
        var n = a.Length;
        var bits = 0;
        while ((1 << bits) < n)
        {
            bits++;
        }

        if ((1 << bits) != n)
        {
            throw new ArgumentException("Length must be a power of 2");
        }

        // Bit reversal permutation
        for (var i = 0; i < n; i++)
        {
            var rev = 0;
            for (var j = 0; j < bits; j++)
            {
                if ((i & (1 << j)) != 0)
                {
                    rev |= (1 << (bits - 1 - j));
                }
            }

            if (i < rev)
            {
                (a[i], a[rev]) = (a[rev], a[i]);
            }
        }

        // Roots of unity
        for (var len = 2; len <= n; len <<= 1)
        {
            var halfLen = len >> 1;
            var root = ModPow(g, (p - 1) / (uint)len, p);
            if (inverse)
            {
                root = ModInverse(root, p);
            }

            for (var i = 0; i < n; i += len)
            {
                uint w = 1;
                for (var j = 0; j < halfLen; j++)
                {
                    var u = a[i + j];
                    var v = ModMul(a[i + halfLen + j], w, p);
                    a[i + j] = ModAdd(u, v, p);
                    a[i + halfLen + j] = ModSub(u, v, p);
                    w = ModMul(w, root, p);
                }
            }
        }

        if (inverse)
        {
            var nInv = ModInverse((uint)n, p);
            for (var i = 0; i < n; i++)
            {
                a[i] = ModMul(a[i], nInv, p);
            }
        }
    }

    /// <summary>
    /// Multiplies two arrays of 32-bit unsigned integers using 3-Prime NTT and Garner's Algorithm.
    /// </summary>
    public static uint[] Multiply(uint[] a, uint[] b)
    {
        var len = a.Length + b.Length;
        var n = 1;
        while (n < len)
        {
            n <<= 1;
        }

        var a1 = new uint[n];
        var a2 = new uint[n];
        var a3 = new uint[n];
        var b1 = new uint[n];
        var b2 = new uint[n];
        var b3 = new uint[n];

        for (var i = 0; i < a.Length; i++)
        {
            a1[i] = a[i] % P1;
            a2[i] = a[i] % P2;
            a3[i] = a[i] % P3;
        }

        for (var i = 0; i < b.Length; i++)
        {
            b1[i] = b[i] % P1;
            b2[i] = b[i] % P2;
            b3[i] = b[i] % P3;
        }

        Ntt(a1, P1, G1, false);
        Ntt(a2, P2, G2, false);
        Ntt(a3, P3, G3, false);

        Ntt(b1, P1, G1, false);
        Ntt(b2, P2, G2, false);
        Ntt(b3, P3, G3, false);

        var c1 = new uint[n];
        var c2 = new uint[n];
        var c3 = new uint[n];

        for (var i = 0; i < n; i++)
        {
            c1[i] = ModMul(a1[i], b1[i], P1);
            c2[i] = ModMul(a2[i], b2[i], P2);
            c3[i] = ModMul(a3[i], b3[i], P3);
        }

        Ntt(c1, P1, G1, true);
        Ntt(c2, P2, G2, true);
        Ntt(c3, P3, G3, true);

        // Garner's Algorithm to reconstruct C
        // X = x1 + x2*P1 + x3*P1*P2
        // X = c1 (mod P1) -> x1 = c1
        // X = c2 (mod P2) -> x1 + x2*P1 = c2 (mod P2) -> x2 = (c2 - x1) * inv(P1, P2) (mod P2)
        // X = c3 (mod P3) -> x1 + x2*P1 + x3*P1*P2 = c3 (mod P3) -> x3 = (c3 - x1 - x2*P1) * inv(P1*P2, P3) (mod P3)

        var invP1_P2 = ModInverse(P1, P2);
        var invP1P2_P3 = ModInverse((uint)(((ulong)P1 * P2) % P3), P3);

        var result = new uint[n];
        ulong carry = 0;

        for (var i = 0; i < n; i++)
        {
            ulong x1 = c1[i];

            var diff2 = c2[i] >= x1 % P2 ? c2[i] - x1 % P2 : c2[i] + P2 - x1 % P2;
            var x2 = (diff2 * invP1_P2) % P2;

            var term12 = (x1 + x2 * P1) % P3;
            var diff3 = c3[i] >= term12 ? c3[i] - term12 : c3[i] + P3 - term12;
            var x3 = (diff3 * invP1P2_P3) % P3;

            ulong p1 = P1;
            ulong p2 = P2;

            var X = (UInt128)x1 + (UInt128)x2 * p1 + (UInt128)x3 * p1 * p2;
            var sum = X + carry;
            result[i] = (uint)(sum & 0xFFFFFFFF);
            carry = (ulong)(sum >> 32);
        }

        return result;
    }
}



