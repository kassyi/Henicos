
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.Kernels;

public class GpuBigIntRnsMontgomeryKernelsTests
{
    [Fact]
    public void TestBaseExtensionShenoyKumaresan_CpuEmulation()
    {
        // K = 8 (256-bit equivalent)
        const int k = 8;
        var b1 = RnsConstantsData.ModuliB1.AsSpan(0, k).ToArray();
        var b2 = RnsConstantsData.ModuliB2.AsSpan(0, k).ToArray();
        var msk = RnsConstantsData.ModulusSK;

        // Calculate M1 and M2
        BigInteger M1 = b1.AsStackEnumerable().Aggregate(BigInteger.One, (acc, m) => acc * m);
        BigInteger M2 = b2.AsStackEnumerable().Aggregate(BigInteger.One, (acc, m) => acc * m);

        // Choose a random value X that is less than M1
        // Let's use a value close to M1 to test the full range
        BigInteger X = M1 / 3 + 123456789;

        // 1. Convert X to RNS in Base 1 and SK
        var srcRnsB1 = new uint[k];
        for (int i = 0; i < k; i++)
        {
            srcRnsB1[i] = (uint)(X % b1[i]);
        }
        uint srcSk = (uint)(X % msk);

        // 2. Base Extension (Base 1 -> Base 2) CPU Emulation
        var destRnsB2 = new uint[k];
        var xi = new uint[k];
        
        // Step 2.1: Calculate xi
        for (int i = 0; i < k; i++)
        {
            // xi = (srcRnsB1[i] * gammaB1[i]) % m1_i
            ulong temp = (ulong)srcRnsB1[i] * RnsConstantsData.GammaB1_8[i];
            xi[i] = (uint)(temp % b1[i]);
        }

        // Step 2.2: Calculate alpha
        // sum_sk = sum(xi * M1OverMiModMsk) mod msk
        ulong sumSk = 0;
        for (int i = 0; i < k; i++)
        {
            ulong term = (ulong)xi[i] * RnsConstantsData.M1OverMiModMsk_8[i];
            sumSk = (sumSk + term) % msk;
        }
        
        // alpha = (sumSk - srcSk) * M1InvModMsk mod msk
        long alphaTemp = (long)sumSk - srcSk;
        if (alphaTemp < 0) alphaTemp += msk;
        ulong alpha = ((ulong)alphaTemp * RnsConstantsData.M1InvModMsk_8) % msk;

        // Step 2.3: Calculate X_j in Base 2
        for (int j = 0; j < k; j++)
        {
            ulong sumB2 = 0;
            for (int i = 0; i < k; i++)
            {
                // M1OverMiModMj is flattened KxK array
                ulong term = (ulong)xi[i] * RnsConstantsData.M1OverMiModMj_8[i * k + j];
                sumB2 = (sumB2 + term) % b2[j];
            }

            ulong alphaTerm = (alpha * RnsConstantsData.M1ModMj_8[j]) % b2[j];
            
            long resultTemp = (long)sumB2 - (long)alphaTerm;
            if (resultTemp < 0) resultTemp += b2[j];
            
            destRnsB2[j] = (uint)resultTemp;
        }

        // 3. Verify against direct conversion of X to Base 2
        var expectedRnsB2 = b2.AsStackEnumerable().Select(m => (uint)(X % m)).ToArray();
        Assert.True(destRnsB2.AsSpan().SequenceEqual(expectedRnsB2));
    }

    [Fact]
    public void TestRnsMontgomeryMultiply_CpuEmulation()
    {
        // Test A * B mod N using RNS Montgomery Reduction
        const int k = 8;
        var b1 = RnsConstantsData.ModuliB1.AsSpan(0, k).ToArray();
        var b2 = RnsConstantsData.ModuliB2.AsSpan(0, k).ToArray();
        var msk = RnsConstantsData.ModulusSK;

        BigInteger M1 = b1.AsStackEnumerable().Aggregate(BigInteger.One, (acc, m) => acc * m);
        BigInteger M2 = b2.AsStackEnumerable().Aggregate(BigInteger.One, (acc, m) => acc * m);

        // Choose N (modulus), must be coprime to M1 and M2.
        // In RNS Montgomery, r_tilde can reach up to (1 + k)*N.
        // We must ensure that r_tilde < M2 to avoid overflow and information loss in Base 2.
        // Therefore, N must be smaller than M2 / (k + 2).
        BigInteger N = M2 / (k + 2);
        if (N % 2 == 0) N -= 1; // Ensure N is odd and likely coprime (or just odd for Montgomery)

        // Choose A and B
        BigInteger A = (N / 3) + 1234;
        BigInteger B = (N / 4) + 5678;

        // True Montgomery result: A * B * M1^-1 mod N
        BigInteger m1InvModN = ModInverse(M1, N);
        BigInteger expected = (A * B * m1InvModN) % N;
        if (expected < 0) expected += N;

        // 1. Setup RNS representations for A, B, N
        var aB1 = new uint[k]; var aB2 = new uint[k];
        var bB1 = new uint[k]; var bB2 = new uint[k];
        var nB2 = new uint[k]; var negNInvB1 = new uint[k];
        var rInvB2 = new uint[k];

        for (int i = 0; i < k; i++)
        {
            aB1[i] = (uint)(A % b1[i]);
            bB1[i] = (uint)(B % b1[i]);
            aB2[i] = (uint)(A % b2[i]);
            bB2[i] = (uint)(B % b2[i]);
            
            nB2[i] = (uint)(N % b2[i]);
            
            // (-N)^-1 mod m_{1,i}
            BigInteger nInv = ModInverse(N, b1[i]);
            long negNInv = -(long)nInv;
            if (negNInv < 0) negNInv += b1[i];
            negNInvB1[i] = (uint)negNInv;

            // R^-1 (M1^-1) mod m_{2,i}
            rInvB2[i] = (uint)ModInverse(M1, b2[i]);
        }

        // 2. Montgomery Step 1: q = A * B * (-N^-1) mod B1
        var qB1 = new uint[k];
        var xiQ = new uint[k];
        for (int i = 0; i < k; i++)
        {
            ulong ab = (ulong)aB1[i] * bB1[i];
            ulong qTemp = (ab % b1[i]) * negNInvB1[i];
            qB1[i] = (uint)(qTemp % b1[i]);
            
            // xi for Approximate Base Extension
            xiQ[i] = (uint)(((ulong)qB1[i] * RnsConstantsData.GammaB1_8[i]) % b1[i]);
        }

        // 3. Approximate Base Extension: q from B1 to B2
        var qB2 = new uint[k];
        for (int j = 0; j < k; j++)
        {
            ulong sumB2 = 0;
            for (int i = 0; i < k; i++)
            {
                ulong term = (ulong)xiQ[i] * RnsConstantsData.M1OverMiModMj_8[i * k + j];
                sumB2 = (sumB2 + term) % b2[j];
            }
            qB2[j] = (uint)sumB2;
        }

        // 4. Montgomery Step 3: r = (A * B + q * N) * R^-1 mod B2
        var rB2 = new uint[k];
        var xiR = new uint[k];
        for (int j = 0; j < k; j++)
        {
            // Montgomery Step 3: r = (A * B + q * N) * R^-1 mod B2
            ulong abB2 = (ulong)aB2[j] * bB2[j];
            ulong qnB2 = (ulong)qB2[j] * nB2[j];
            ulong rTemp = ((abB2 % b2[j]) + (qnB2 % b2[j])) % b2[j];
            rB2[j] = (uint)((rTemp * rInvB2[j]) % b2[j]);
            
            // xi for Shenoy-Kumaresan Base Extension (B2 -> B1)
            xiR[j] = (uint)(((ulong)rB2[j] * RnsConstantsData.GammaB2_8[j]) % b2[j]);
        }

        // 5. Calculate r_sk for the final Base Extension
        // We need r_sk = (a_sk * b_sk + q_sk * n_sk) * M1^-1 mod m_sk
        uint aSk = (uint)(A % msk);
        uint bSk = (uint)(B % msk);
        uint nSk = (uint)(N % msk);
        
        // Calculate q_sk using Approximate Base Extension from B1
        ulong qSkSum = 0;
        for (int i = 0; i < k; i++)
        {
            ulong term = (ulong)xiQ[i] * RnsConstantsData.M1OverMiModMsk_8[i];
            qSkSum = (qSkSum + term) % msk;
        }
        uint qSk = (uint)qSkSum;

        ulong abSk = (ulong)aSk * bSk;
        ulong qnSk = (ulong)qSk * nSk;
        ulong rTempSk = ((abSk % msk) + (qnSk % msk)) % msk;
        uint rSk = (uint)((rTempSk * RnsConstantsData.M1InvModMsk_8) % msk);

        // 6. Shenoy-Kumaresan Base Extension: r from B2 to B1
        var rB1 = new uint[k];
        
        // Calculate alpha for B2 -> B1
        ulong sumSkR = 0;
        for (int j = 0; j < k; j++)
        {
            ulong term = (ulong)xiR[j] * RnsConstantsData.M2OverMjModMsk_8[j];
            sumSkR = (sumSkR + term) % msk;
        }
        
        long alphaTempR = (long)sumSkR - rSk;
        if (alphaTempR < 0) alphaTempR += msk;
        ulong alphaR = ((ulong)alphaTempR * RnsConstantsData.M2InvModMsk_8) % msk;

        // Calculate r in Base 1
        for (int i = 0; i < k; i++)
        {
            ulong sumB1 = 0;
            for (int j = 0; j < k; j++)
            {
                // M2OverMjModMi_K is KxK flattened
                ulong term = (ulong)xiR[j] * RnsConstantsData.M2OverMjModMi_8[j * k + i];
                sumB1 = (sumB1 + term) % b1[i];
            }

            ulong alphaTermR = (alphaR * RnsConstantsData.M2ModMi_8[i]) % b1[i];
            
            long resultTempR = (long)sumB1 - (long)alphaTermR;
            if (resultTempR < 0) resultTempR += b1[i];
            
            rB1[i] = (uint)resultTempR;
        }

        // 7. Verification
        // The output 'rB1' and 'rB2' represents r = (A * B * M1^-1) + C * N.
        // It might not be strictly less than N, but it's congruent modulo N.
        // Let's reconstruct the BigInteger value from rB1 and B1 to check.
        BigInteger R_reconstructed = 0;
        for (int i = 0; i < k; i++)
        {
            BigInteger m1_i = b1[i];
            BigInteger M1_over_i = M1 / m1_i;
            BigInteger gamma_i = ModInverse(M1_over_i, m1_i);
            
            BigInteger term = (rB1[i] * gamma_i) % m1_i;
            term = (term * M1_over_i);
            R_reconstructed += term;
        }
        R_reconstructed %= M1;

        // Check if R_reconstructed is congruent to 'expected' modulo N
        Assert.Equal(expected, R_reconstructed % N);
    }

    private BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        BigInteger m0 = m, t, q;
        BigInteger x0 = 0, x1 = 1;

        if (m == 1) return 0;
        while (a > 1)
        {
            q = a / m;
            t = m;
            m = a % m;
            a = t;
            t = x0;
            x0 = x1 - q * x0;
            x1 = t;
        }
        if (x1 < 0) x1 += m0;
        return x1;
    }
}
