namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

/// <summary>
/// Implements fully parallel, carry-free modular exponentiation using Residue Number System (RNS) Montgomery Reduction.
/// Targeted for sizes where NTT overhead is too high, but Schoolbook/DivRem carry propagation stalls the GPU (~256 to 1024 bits).
/// </summary>
[SkipSnapshotConsistency("WIP - Phase 2 RNS Montgomery")]
public static partial class GpuBigIntRnsMontgomeryKernels
{
    // =========================================================================
    // RNS Moduli Configuration (Pre-selected 32-bit Pseudo-Mersenne Primes)
    // =========================================================================

    // RNS Moduli Configuration and Primes are now auto-generated via RnsConstantsGenerator
    // See: GpuBigIntRnsMontgomeryKernels.Constants.g.cs


    // =========================================================================
    // Core RNS Montgomery Operations
    // =========================================================================

    [GpuDeviceFunction]
    public static void ExecuteRnsMontgomeryMultiply(RnsBigInt a, RnsBigInt b, RnsMontgomeryContext montCtx,
        RnsConstantsContext ctx, RnsBigInt result)
    {
        var tid = ThreadIndex.X;
        if (tid >= ctx.KSize)
        {
            return;
        }

        var m1 = ctx.ModuliB1[tid];
        var m2 = ctx.ModuliB2[tid];

        // Shared memory arrays for intermediate xi (Maximum supported K is 128)
        var xiQShared = new SharedMemory<uint>(128);
        var xiRShared = new SharedMemory<uint>(128);

        // ========================================================
        // 1. q = a * b * (-N^{-1}) mod Base 1
        // ========================================================
        ulong abB1 = (ulong)a.B1[tid] * b.B1[tid];
        ulong qTemp = (abB1 % m1) * montCtx.NegNInvB1[tid];
        uint qB1 = (uint)(qTemp % m1);

        // xi for Approximate Base Extension
        xiQShared[tid] = (uint)(((ulong)qB1 * ctx.GammaB1[tid]) % m1);

        Gpu.SyncThreads();

        // ========================================================
        // 2. Approximate Base Extension: q from Base 1 to Base 2
        // ========================================================
        ulong qB2 = 0;
        for (int i = 0; i < ctx.KSize; i++)
        {
            ulong term = (ulong)xiQShared[i] * ctx.M1OverMiModMj[i * ctx.KSize + tid];
            qB2 = (qB2 + term) % m2;
        }

        // ========================================================
        // 3. r = (a * b + q * N) * R^{-1} mod Base 2
        // ========================================================
        ulong abB2 = (ulong)a.B2[tid] * b.B2[tid];
        ulong qnB2 = qB2 * montCtx.NRnsB2[tid];
        ulong rTempB2 = ((abB2 % m2) + (qnB2 % m2)) % m2;
        uint rB2 = (uint)((rTempB2 * montCtx.RInvB2[tid]) % m2);

        // xi for Shenoy-Kumaresan Base Extension (B2 -> B1)
        xiRShared[tid] = (uint)(((ulong)rB2 * ctx.GammaB2[tid]) % m2);

        // Output Base 2 result
        result.B2[tid] = rB2;

        Gpu.SyncThreads();

        // ========================================================
        // 4. Shenoy-Kumaresan Base Extension: r from Base 2 to Base 1
        // ========================================================

        // Calculate q_sk and sumSkR redundantly in all threads to avoid extra shared memory
        ulong qSkSum = 0;
        ulong sumSkR = 0;
        for (int i = 0; i < ctx.KSize; i++)
        {
            ulong termQ = (ulong)xiQShared[i] * ctx.M1OverMiModMsk[i];
            qSkSum = (qSkSum + termQ) % ctx.ModSk;

            ulong termR = (ulong)xiRShared[i] * ctx.M2OverMjModMsk[i];
            sumSkR = (sumSkR + termR) % ctx.ModSk;
        }

        // Calculate r_sk
        ulong abSk = (ulong)a.Sk * b.Sk;
        ulong qnSk = qSkSum * montCtx.NSk;
        ulong rTempSk = ((abSk % ctx.ModSk) + (qnSk % ctx.ModSk)) % ctx.ModSk;
        uint rSk = (uint)((rTempSk * ctx.M1InvModMsk) % ctx.ModSk);

        // Calculate alpha for B2 -> B1
        long alphaTempR = (long)sumSkR - rSk;
        if (alphaTempR < 0)
        {
            alphaTempR += ctx.ModSk;
        }

        ulong alphaR = ((ulong)alphaTempR * ctx.M2InvModMsk) % ctx.ModSk;

        // Calculate r in Base 1
        ulong sumB1 = 0;
        for (int j = 0; j < ctx.KSize; j++)
        {
            ulong term = (ulong)xiRShared[j] * ctx.M2OverMjModMi[j * ctx.KSize + tid];
            sumB1 = (sumB1 + term) % m1;
        }

        ulong alphaTermR = (alphaR * ctx.M2ModMi[tid]) % m1;

        long resultTempR = (long)sumB1 - (long)alphaTermR;
        if (resultTempR < 0)
        {
            resultTempR += m1;
        }

        result.B1[tid] = (uint)resultTempR;
    }


    // =========================================================================
    // RNS ModPow Loop (Square-and-Multiply in Montgomery Space)
    // =========================================================================

    [GpuKernel]
    public static void ExecuteModPowRns(
        RnsBigInt baseRns, // Pre-converted to Montgomery Space: Base * M1 mod N
        RnsBigInt oneRns,  // The value 1 in Montgomery Space: M1 mod N
        [GpuReadOnly] uint[] exponent,
        int exponentWords,
        RnsMontgomeryContext montCtx,
        RnsConstantsContext ctx,
        RnsBigInt result,
        RnsBigInt x,       // Workspace 1
        RnsBigInt temp)    // Workspace 2
    {
        var tid = ThreadIndex.X;
        if (tid >= ctx.KSize)
        {
            return;
        }

        // Initialization
        result.B1[tid] = oneRns.B1[tid];
        result.B2[tid] = oneRns.B2[tid];

        x.B1[tid] = baseRns.B1[tid];
        x.B2[tid] = baseRns.B2[tid];

        if (tid == 0)
        {
            result.Sk = oneRns.Sk;
            x.Sk = baseRns.Sk;
        }

        Gpu.SyncThreads();

        // Square and Multiply
        for (int i = 0; i < exponentWords; i++)
        {
            uint eWord = exponent[i];

            for (int b = 0; b < 32; b++)
            {
                bool bitSet = ((eWord >> b) & 1) == 1;

                // Always compute temp = result * x to maintain Constant-Time execution and prevent SyncThreads in branch divergence
                ExecuteRnsMontgomeryMultiply(result, x, montCtx, ctx, temp);
                Gpu.SyncThreads();

                if (bitSet)
                {
                    // If bit is 1, keep the multiplied result
                    result.B1[tid] = temp.B1[tid];
                    result.B2[tid] = temp.B2[tid];
                    if (tid == 0)
                    {
                        result.Sk = temp.Sk;
                    }
                }
                Gpu.SyncThreads();

                // x = x * x
                ExecuteRnsMontgomeryMultiply(x, x, montCtx, ctx, temp);
                Gpu.SyncThreads();

                // x = temp
                x.B1[tid] = temp.B1[tid];
                x.B2[tid] = temp.B2[tid];
                if (tid == 0)
                {
                    x.Sk = temp.Sk;
                }

                Gpu.SyncThreads();
            }
        }

        // Final Montgomery Reduction to exit Montgomery Space
        // Multiply by 1 in normal RNS space
        temp.B1[tid] = 1;
        temp.B2[tid] = 1;
        if (tid == 0)
        {
            temp.Sk = 1;
        }

        Gpu.SyncThreads();

        // Use x as the final destination temporary
        ExecuteRnsMontgomeryMultiply(result, temp, montCtx, ctx, x);
        Gpu.SyncThreads();

        // Copy final result back to result buffer
        result.B1[tid] = x.B1[tid];
        result.B2[tid] = x.B2[tid];
        if (tid == 0)
        {
            result.Sk = x.Sk;
        }
    }
}
