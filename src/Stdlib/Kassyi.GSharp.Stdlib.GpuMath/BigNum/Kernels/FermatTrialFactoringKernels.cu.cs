namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[SkipSnapshotConsistency("New Feature WIP")]
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static partial class FermatTrialFactoringKernels
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
    public static void SearchFactors32Kernel(
        uint startOdd,
        uint exp,
        uint[] results,
        uint[] resultCount,
        int maxResults)
    {
        var threadId = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        var k = startOdd + ((ulong)threadId * 2UL);

        // N(x) = 2x^2 - x + 11. x = 2^{exp mod (k-1)} mod k
        var ePrime = exp % (k - 1UL);
        var x = Device_ModPow64_Under32Bit(2UL, ePrime, k);

        var x2 = (x * x) % k;
        var term1 = (2UL * x2) % k;

        var nModK = (term1 + k - x + 11UL) % k;

        if (nModK == 0UL)
        {
            var idx = Gpu.AtomicAdd(ref resultCount[0], 1U);
            if (idx < (uint)maxResults)
            {
                results[idx] = (uint)k;
            }
        }
    }
}
