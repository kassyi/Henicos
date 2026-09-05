namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

[SkipSnapshotConsistency("New Feature WIP")]
public static class FermatTrialFactoringKernels
{
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
    public static void Kernel_SearchFactors32(
        uint startOdd,
        uint E,
        uint[] results,
        uint[] resultCount,
        int maxResults)
    {
        var threadId = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        var k = (ulong)startOdd + ((ulong)threadId * 2UL);

        // N(X) = 2X^2 - X + 11. X = 2^{E mod (k-1)} mod k
        var e_prime = (ulong)E % (k - 1UL);
        var X = Device_ModPow64_Under32Bit(2UL, e_prime, k);

        var x2 = (X * X) % k;
        var term1 = (2UL * x2) % k;

        var N_mod_k = (term1 + k - X + 11UL) % k;

        if (N_mod_k == 0UL)
        {
            var idx = Gpu.AtomicAdd(ref resultCount[0], 1U);
            if (idx < (uint)maxResults)
            {
                results[idx] = (uint)k;
            }
        }
    }
}
