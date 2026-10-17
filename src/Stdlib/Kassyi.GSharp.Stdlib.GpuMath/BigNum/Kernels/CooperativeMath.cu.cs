namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
#pragma warning disable GSH0014 // Justification: Lambda capture limitations in source generation prevent passing context by reference.
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class CooperativeMath
{
    /// <summary>
    /// Computes the 32-bit Montgomery reduction constant <c>mu = -n0^{-1} mod 2^32</c> for an odd least-significant modulus word <paramref name="n0"/>
    /// using 5 iterations of Hensel's Lemma / Newton-Raphson lifting.
    /// </summary>
    /// <param name="n0">The least-significant 32-bit word of an odd modulus (<c>n0 &amp; 1 == 1</c>).</param>
    /// <returns>The Montgomery constant <c>mu</c> satisfying <c>n0 * mu == -1 (mod 2^32)</c>.</returns>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ComputeMontgomeryMu(uint n0)
    {
        uint inv = 1u;
        for (int i = 0; i < 5; i++)
        {
            inv *= 2u - n0 * inv;
        }

        return (uint)-(int)inv;
    }

    /// <summary>
    /// Performs warp-cooperative Montgomery reduction (<c>result = multiLimbValue * R^{-1} mod modulus</c> where <c>R = 2^{32 * k}</c>)
    /// purely using warp shuffle intrinsics and carry-chain additions without shared memory.
    /// </summary>
    /// <param name="multiLimbValue">The <c>2 * k</c>-word input integer <c>T</c> (<c>0 &lt;= T &lt; modulus * R</c>).</param>
    /// <param name="modulus">The <c>k</c>-word odd modulus <c>N</c>.</param>
    /// <param name="mu">The precomputed Montgomery constant <c>-N[0]^{-1} mod 2^32</c>.</param>
    /// <param name="result">The <c>k</c>-word destination buffer receiving <c>T * R^{-1} mod N</c>.</param>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MontgomeryReduceWarpSync(
        [GpuReadOnly] Span<uint> multiLimbValue,
        [GpuReadOnly] Span<uint> modulus,
        uint mu,
        Span<uint> result)
    {
        MontgomeryReduceWarpSync(multiLimbValue, modulus, mu, result, modulus.Length);
    }

    /// <summary>
    /// Performs warp-cooperative Montgomery reduction (<c>result = multiLimbValue * R^{-1} mod modulus</c>) for an explicit word count <paramref name="numWords"/>.
    /// </summary>
    /// <param name="multiLimbValue">The <c>2 * numWords</c>-word input integer <c>T</c> (<c>0 &lt;= T &lt; modulus * R</c>).</param>
    /// <param name="modulus">The <c>numWords</c>-word odd modulus <c>N</c>.</param>
    /// <param name="mu">The precomputed Montgomery constant <c>-N[0]^{-1} mod 2^32</c>.</param>
    /// <param name="result">The <c>numWords</c>-word destination buffer receiving <c>T * R^{-1} mod N</c>.</param>
    /// <param name="numWords">The number of 32-bit words in the modulus.</param>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MontgomeryReduceWarpSync(
        [GpuReadOnly] Span<uint> multiLimbValue,
        [GpuReadOnly] Span<uint> modulus,
        uint mu,
        Span<uint> result,
        int numWords)
    {
        int laneId = ThreadIndex.X & 31;

        // Initialize result[0..numWords-1] with multiLimbValue[0..numWords-1]
        for (int offset = 0; offset < numWords; offset += 32)
        {
            int idx = offset + laneId;
            if (idx < numWords)
            {
                result[idx] = multiLimbValue[idx];
            }
        }

        uint metaCarry = 0u;
        int lastWordLane = (numWords - 1) & 31;

        for (int i = 0; i < numWords; i++)
        {
            uint t0 = laneId == 0 ? result[0] : 0u;
            t0 = Warp.ShuffleSync(0xFFFFFFFFu, t0, 0);
            uint m = t0 * mu;

            uint chunkCarry1 = 0u;
            uint chunkCarry2 = 0u;
            uint chunkHighIn = 0u;
            uint lastHighProd = 0u;

            for (int offset = 0; offset < numWords; offset += 32)
            {
                int idx = offset + laneId;
                bool valid = idx < numWords;

                uint rVal = valid ? result[idx] : 0u;
                uint nVal = valid ? modulus[idx] : 0u;

                ulong prod = Gpu.MultiplyWide(m, nVal);
                uint lowProd = (uint)prod;
                uint highProd = (uint)(prod >> 32);

                // Pass 1: Add lowProd to rVal and propagate carries across the warp
                ulong c1 = 0ul;
                uint sum1 = Gpu.AddWithCarry(rVal, lowProd, ref c1);
                uint g1 = valid ? (uint)c1 : 0u;
                uint p1 = valid ? (sum1 == 0xFFFFFFFFu ? 1u : 0u) : 1u;

                var scan1 = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g1, p1), laneId);
                uint prevG1 = Warp.ShuffleUpSync(0xFFFFFFFFu, scan1.Generate, 1u);
                uint prevP1 = Warp.ShuffleUpSync(0xFFFFFFFFu, scan1.Propagate, 1u);

                uint carryIn1 = chunkCarry1;
                if (laneId > 0)
                {
                    carryIn1 = prevG1 | (prevP1 & carryIn1);
                }

                uint valAfterPass1 = sum1 + carryIn1;
                uint laneCarryOut1 = GpuBigIntKernels.ApplyCarry(scan1, chunkCarry1);
                chunkCarry1 = Warp.ShuffleSync(0xFFFFFFFFu, laneCarryOut1, 31);

                // Pass 2: Shift highProd up by 1 lane and add to valAfterPass1
                uint prevHigh = Warp.ShuffleUpSync(0xFFFFFFFFu, highProd, 1u);
                if (laneId == 0)
                {
                    prevHigh = chunkHighIn;
                }

                if (!valid)
                {
                    prevHigh = 0u;
                }

                ulong c2 = 0ul;
                uint sum2 = Gpu.AddWithCarry(valAfterPass1, prevHigh, ref c2);
                uint g2 = valid ? (uint)c2 : 0u;
                uint p2 = valid ? (sum2 == 0xFFFFFFFFu ? 1u : 0u) : 1u;

                var scan2 = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g2, p2), laneId);
                uint prevG2 = Warp.ShuffleUpSync(0xFFFFFFFFu, scan2.Generate, 1u);
                uint prevP2 = Warp.ShuffleUpSync(0xFFFFFFFFu, scan2.Propagate, 1u);

                uint carryIn2 = chunkCarry2;
                if (laneId > 0)
                {
                    carryIn2 = prevG2 | (prevP2 & carryIn2);
                }

                uint finalWord = sum2 + carryIn2;
                uint laneCarryOut2 = GpuBigIntKernels.ApplyCarry(scan2, chunkCarry2);
                chunkCarry2 = Warp.ShuffleSync(0xFFFFFFFFu, laneCarryOut2, 31);
                chunkHighIn = Warp.ShuffleSync(0xFFFFFFFFu, highProd, 31);

                if (offset + 32 >= numWords)
                {
                    lastHighProd = Warp.ShuffleSync(0xFFFFFFFFu, highProd, lastWordLane);
                }

                // Shift right by 1 word while preserving owner-computes affinity:
                // If offset > 0, word 0 of this chunk belongs to index (offset - 1), owned by lane 31.
                if (offset > 0)
                {
                    uint chunkFirstWord = Warp.ShuffleSync(0xFFFFFFFFu, finalWord, 0);
                    if (laneId == 31)
                    {
                        result[offset - 1] = chunkFirstWord;
                    }
                }

                uint nextLaneWord = Warp.ShuffleDownSync(0xFFFFFFFFu, finalWord, 1u);
                if (laneId < 31 && (idx + 1) < numWords)
                {
                    result[idx] = nextLaneWord;
                }
            }

            ulong topSum = (ulong)metaCarry + multiLimbValue[i + numWords] + lastHighProd + chunkCarry1 + chunkCarry2;
            if (laneId == lastWordLane)
            {
                result[numWords - 1] = (uint)topSum;
            }

            metaCarry = (uint)(topSum >> 32);
        }

        ConditionalSubtractModulusWarpSync(result, modulus, metaCarry, numWords);
    }

    /// <summary>
    /// Performs warp-cooperative Coarsely Integrated Operand Scanning (CIOS) Montgomery multiplication
    /// (<c>result = a * b * R^{-1} mod modulus</c>) purely in warp registers without shared memory.
    /// </summary>
    /// <param name="a">The first <c>k</c>-word operand in Montgomery space (<c>0 &lt;= a &lt; modulus</c>).</param>
    /// <param name="b">The second <c>k</c>-word operand in Montgomery space (<c>0 &lt;= b &lt; modulus</c>).</param>
    /// <param name="modulus">The <c>k</c>-word odd modulus <c>N</c>.</param>
    /// <param name="mu">The precomputed Montgomery constant <c>-N[0]^{-1} mod 2^32</c>.</param>
    /// <param name="result">The <c>k</c>-word destination buffer receiving <c>a * b * R^{-1} mod N</c>.</param>
    /// <param name="numWords">The number of 32-bit words in the modulus and operands.</param>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MontgomeryMultiplyWarpSync(
        [GpuReadOnly] Span<uint> a,
        [GpuReadOnly] Span<uint> b,
        [GpuReadOnly] Span<uint> modulus,
        uint mu,
        Span<uint> result,
        int numWords)
    {
        int laneId = ThreadIndex.X & 31;

        for (int offset = 0; offset < numWords; offset += 32)
        {
            int idx = offset + laneId;
            if (idx < numWords)
            {
                result[idx] = 0u;
            }
        }

        uint metaCarry = 0u;
        int lastWordLane = (numWords - 1) & 31;

        for (int i = 0; i < numWords; i++)
        {
            uint bi = b[i];

            // Step 1: result += bi * a
            uint chunkCarryA1 = 0u;
            uint chunkCarryA2 = 0u;
            uint chunkHighInA = 0u;
            uint lastHighProdA = 0u;

            for (int offset = 0; offset < numWords; offset += 32)
            {
                int idx = offset + laneId;
                bool valid = idx < numWords;

                uint rVal = valid ? result[idx] : 0u;
                uint aVal = valid ? a[idx] : 0u;

                ulong prodA = Gpu.MultiplyWide(bi, aVal);
                uint lowProdA = (uint)prodA;
                uint highProdA = (uint)(prodA >> 32);

                ulong c1 = 0ul;
                uint sum1 = Gpu.AddWithCarry(rVal, lowProdA, ref c1);
                uint g1 = valid ? (uint)c1 : 0u;
                uint p1 = valid ? (sum1 == 0xFFFFFFFFu ? 1u : 0u) : 1u;

                var scan1 = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g1, p1), laneId);
                uint prevG1 = Warp.ShuffleUpSync(0xFFFFFFFFu, scan1.Generate, 1u);
                uint prevP1 = Warp.ShuffleUpSync(0xFFFFFFFFu, scan1.Propagate, 1u);

                uint carryIn1 = chunkCarryA1;
                if (laneId > 0)
                {
                    carryIn1 = prevG1 | (prevP1 & carryIn1);
                }

                uint valAfterPass1 = sum1 + carryIn1;
                uint laneCarryOut1 = GpuBigIntKernels.ApplyCarry(scan1, chunkCarryA1);
                chunkCarryA1 = Warp.ShuffleSync(0xFFFFFFFFu, laneCarryOut1, 31);

                uint prevHighA = Warp.ShuffleUpSync(0xFFFFFFFFu, highProdA, 1u);
                if (laneId == 0)
                {
                    prevHighA = chunkHighInA;
                }

                if (!valid)
                {
                    prevHighA = 0u;
                }

                ulong c2 = 0ul;
                uint sum2 = Gpu.AddWithCarry(valAfterPass1, prevHighA, ref c2);
                uint g2 = valid ? (uint)c2 : 0u;
                uint p2 = valid ? (sum2 == 0xFFFFFFFFu ? 1u : 0u) : 1u;

                var scan2 = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g2, p2), laneId);
                uint prevG2 = Warp.ShuffleUpSync(0xFFFFFFFFu, scan2.Generate, 1u);
                uint prevP2 = Warp.ShuffleUpSync(0xFFFFFFFFu, scan2.Propagate, 1u);

                uint carryIn2 = chunkCarryA2;
                if (laneId > 0)
                {
                    carryIn2 = prevG2 | (prevP2 & carryIn2);
                }

                uint finalWordA = sum2 + carryIn2;
                if (valid)
                {
                    result[idx] = finalWordA;
                }

                uint laneCarryOut2 = GpuBigIntKernels.ApplyCarry(scan2, chunkCarryA2);
                chunkCarryA2 = Warp.ShuffleSync(0xFFFFFFFFu, laneCarryOut2, 31);
                chunkHighInA = Warp.ShuffleSync(0xFFFFFFFFu, highProdA, 31);

                if (offset + 32 >= numWords)
                {
                    lastHighProdA = Warp.ShuffleSync(0xFFFFFFFFu, highProdA, lastWordLane);
                }
            }

            ulong topAcc = (ulong)metaCarry + lastHighProdA + chunkCarryA1 + chunkCarryA2;

            // Step 2: m = result[0] * mu, result = (result + m * modulus) >> 32
            uint t0 = laneId == 0 ? result[0] : 0u;
            t0 = Warp.ShuffleSync(0xFFFFFFFFu, t0, 0);
            uint m = t0 * mu;

            uint chunkCarryN1 = 0u;
            uint chunkCarryN2 = 0u;
            uint chunkHighInN = 0u;
            uint lastHighProdN = 0u;

            for (int offset = 0; offset < numWords; offset += 32)
            {
                int idx = offset + laneId;
                bool valid = idx < numWords;

                uint rVal = valid ? result[idx] : 0u;
                uint nVal = valid ? modulus[idx] : 0u;

                ulong prodN = Gpu.MultiplyWide(m, nVal);
                uint lowProdN = (uint)prodN;
                uint highProdN = (uint)(prodN >> 32);

                ulong c1 = 0ul;
                uint sum1 = Gpu.AddWithCarry(rVal, lowProdN, ref c1);
                uint g1 = valid ? (uint)c1 : 0u;
                uint p1 = valid ? (sum1 == 0xFFFFFFFFu ? 1u : 0u) : 1u;

                var scan1 = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g1, p1), laneId);
                uint prevG1 = Warp.ShuffleUpSync(0xFFFFFFFFu, scan1.Generate, 1u);
                uint prevP1 = Warp.ShuffleUpSync(0xFFFFFFFFu, scan1.Propagate, 1u);

                uint carryIn1 = chunkCarryN1;
                if (laneId > 0)
                {
                    carryIn1 = prevG1 | (prevP1 & carryIn1);
                }

                uint valAfterPass1 = sum1 + carryIn1;
                uint laneCarryOut1 = GpuBigIntKernels.ApplyCarry(scan1, chunkCarryN1);
                chunkCarryN1 = Warp.ShuffleSync(0xFFFFFFFFu, laneCarryOut1, 31);

                uint prevHighN = Warp.ShuffleUpSync(0xFFFFFFFFu, highProdN, 1u);
                if (laneId == 0)
                {
                    prevHighN = chunkHighInN;
                }

                if (!valid)
                {
                    prevHighN = 0u;
                }

                ulong c2 = 0ul;
                uint sum2 = Gpu.AddWithCarry(valAfterPass1, prevHighN, ref c2);
                uint g2 = valid ? (uint)c2 : 0u;
                uint p2 = valid ? (sum2 == 0xFFFFFFFFu ? 1u : 0u) : 1u;

                var scan2 = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g2, p2), laneId);
                uint prevG2 = Warp.ShuffleUpSync(0xFFFFFFFFu, scan2.Generate, 1u);
                uint prevP2 = Warp.ShuffleUpSync(0xFFFFFFFFu, scan2.Propagate, 1u);

                uint carryIn2 = chunkCarryN2;
                if (laneId > 0)
                {
                    carryIn2 = prevG2 | (prevP2 & carryIn2);
                }

                uint finalWordN = sum2 + carryIn2;
                uint laneCarryOut2 = GpuBigIntKernels.ApplyCarry(scan2, chunkCarryN2);
                chunkCarryN2 = Warp.ShuffleSync(0xFFFFFFFFu, laneCarryOut2, 31);
                chunkHighInN = Warp.ShuffleSync(0xFFFFFFFFu, highProdN, 31);

                if (offset + 32 >= numWords)
                {
                    lastHighProdN = Warp.ShuffleSync(0xFFFFFFFFu, highProdN, lastWordLane);
                }

                if (offset > 0)
                {
                    uint chunkFirstWord = Warp.ShuffleSync(0xFFFFFFFFu, finalWordN, 0);
                    if (laneId == 31)
                    {
                        result[offset - 1] = chunkFirstWord;
                    }
                }

                uint nextLaneWord = Warp.ShuffleDownSync(0xFFFFFFFFu, finalWordN, 1u);
                if (laneId < 31 && (idx + 1) < numWords)
                {
                    result[idx] = nextLaneWord;
                }
            }

            ulong topSum = topAcc + lastHighProdN + chunkCarryN1 + chunkCarryN2;
            if (laneId == lastWordLane)
            {
                result[numWords - 1] = (uint)topSum;
            }

            metaCarry = (uint)(topSum >> 32);
        }

        ConditionalSubtractModulusWarpSync(result, modulus, metaCarry, numWords);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConditionalSubtractModulusWarpSync(
        Span<uint> result,
        [GpuReadOnly] Span<uint> modulus,
        uint metaCarry,
        int numWords)
    {
        int laneId = ThreadIndex.X & 31;
        uint warpCarryIn = 1u;

        // Pass 1: Determine whether result >= modulus (warpCarryIn == 1 means no borrow)
        for (int offset = 0; offset < numWords; offset += 32)
        {
            int idx = offset + laneId;
            bool valid = idx < numWords;

            uint valA = valid ? result[idx] : 0u;
            uint valB = valid ? ~modulus[idx] : 0xFFFFFFFFu;

            ulong sum = (ulong)valA + valB;
            uint g = valid ? (uint)(sum >> 32) : 0u;
            uint p = valid ? ((uint)sum == 0xFFFFFFFFu ? 1u : 0u) : 1u;

            var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);
            uint laneCarryOut = GpuBigIntKernels.ApplyCarry(warpScan, warpCarryIn);
            warpCarryIn = Warp.ShuffleSync(0xFFFFFFFFu, laneCarryOut, 31);
        }

        if (metaCarry != 0u || warpCarryIn != 0u)
        {
            ParallelAdder.SubParallelWarpSync(result, modulus, result, numWords);
        }
    }

    [GpuKernel]
    public static void WarpMontgomeryReduceKernel(
        [GpuReadOnly] uint[] multiLimbValue,
        [GpuReadOnly] uint[] modulus,
        uint mu,
        uint[] result,
        int numWords)
    {
        if (BlockIndex.X != 0 || ThreadIndex.X >= 32)
        {
            return;
        }

        if (numWords == modulus.Length)
        {
            MontgomeryReduceWarpSync(multiLimbValue, modulus, mu, result);
        }
        else
        {
            MontgomeryReduceWarpSync(multiLimbValue, modulus, mu, result, numWords);
        }
    }

    [GpuKernel]
    public static void WarpMontgomeryMultiplyKernel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        [GpuReadOnly] uint[] modulus,
        uint mu,
        uint[] result,
        int numWords)
    {
        if (BlockIndex.X != 0 || ThreadIndex.X >= 32)
        {
            return;
        }

        MontgomeryMultiplyWarpSync(a, b, modulus, mu, result, numWords);
    }
}
