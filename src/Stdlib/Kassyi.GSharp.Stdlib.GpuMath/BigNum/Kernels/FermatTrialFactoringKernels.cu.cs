namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[SkipSnapshotConsistency("New Feature WIP")]
[ExcludeFromCodeCoverage]
public static partial class FermatTrialFactoringKernels
{
    public const uint QuadraticResidueMaskMod29 = 0x13D122F2u;

    [GpuDeviceFunction, GpuInline]
    public static bool PassesQuadraticAndSmallPrimeSieve(uint k)
    {
        if (k == 3u || k == 29u)
        {
            return true;
        }

        bool legMinus3IsOne = (k % 3u) == 1u;
        uint rem29 = k % 29u;
        bool leg29IsOne = ((QuadraticResidueMaskMod29 >> (int)rem29) & 1u) != 0u;
        if (legMinus3IsOne != leg29IsOne)
        {
            return false;
        }

        if (k > 29u)
        {
            if (k % 3u == 0u || k % 5u == 0u || k % 7u == 0u ||
                k % 11u == 0u || k % 13u == 0u || k % 17u == 0u ||
                k % 19u == 0u || k % 23u == 0u || rem29 == 0u)
            {
                return false;
            }
        }

        return true;
    }

    [GpuDeviceFunction, GpuInline]
    public static bool PassesQuadraticAndSmallPrimeSieve(ulong k)
    {
        if (k <= 0xFFFFFFFFUL)
        {
            return PassesQuadraticAndSmallPrimeSieve((uint)k);
        }

        if (k == 3UL || k == 29UL)
        {
            return true;
        }

        bool legMinus3IsOne = (k % 3UL) == 1UL;
        int rem29 = (int)(k % 29UL);
        bool leg29IsOne = ((QuadraticResidueMaskMod29 >> rem29) & 1u) != 0u;
        if (legMinus3IsOne != leg29IsOne)
        {
            return false;
        }

        if (k % 3UL == 0UL || k % 5UL == 0UL || k % 7UL == 0UL ||
            k % 11UL == 0UL || k % 13UL == 0UL || k % 17UL == 0UL ||
            k % 19UL == 0UL || k % 23UL == 0UL || rem29 == 0)
        {
            return false;
        }

        return true;
    }

    [GpuDeviceFunction, GpuInline]
    public static ulong Device_ModPow64_Under32Bit(ulong baseVal, ulong exp, ulong m)
    {
        ulong res = 1;
        baseVal %= m;
        while (exp > 0)
        {
            if ((exp & 1) != 0)
            {
                res = (res * baseVal) % m;
            }

            baseVal = (baseVal * baseVal) % m;
            exp >>= 1;
        }

        return res;
    }

    [GpuKernel]
    public static void SearchFactors32Kernel(
        uint startOdd,
        uint exp,
        uint[] results,
        uint[] resultCount,
        int maxResults)
    {
        var threadId = (uint)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        var k32 = startOdd + (threadId * 2u);

        if (!PassesQuadraticAndSmallPrimeSieve(k32))
        {
            return;
        }

        ulong k = k32;

        // N(x) = 2x^2 - x + 11. x = 2^{exp mod (k-1)} mod k
        var ePrime = exp % (k32 - 1u);
        var x = Device_ModPow64_Under32Bit(2UL, ePrime, k);

        var x2 = (x * x) % k;
        var term1 = (2UL * x2) % k;

        var nModK = (term1 + k - x + 11UL) % k;

        if (nModK == 0UL)
        {
            var idx = Gpu.AtomicAdd(ref resultCount[0], 1U);
            if (idx < (uint)maxResults)
            {
                results[idx] = k32;
            }
        }
    }
}

