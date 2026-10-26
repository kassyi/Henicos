using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.Kernels;

public class GpuBigIntNttReciprocalTests
{
    [Fact]
    public void TestNewtonRaphsonReciprocal_CpuEmulation()
    {
        // =========================================================================
        // Target: Calculate X = floor(2^(2k) / D) using Newton-Raphson iteration
        // Iteration: X_{i+1} = floor(X_i * (2^{2k+1} - D * X_i) / 2^{2k})
        // =========================================================================

        // 1. Setup a giant number D (e.g., 512 bits)
        var rng = new Random(42);
        byte[] dBytes = new byte[64]; // 512 bits
        rng.NextBytes(dBytes);
        dBytes[63] |= 0x80; // Ensure it's exactly 512 bits (highest bit set)
        
        BigInteger D = new BigInteger(dBytes, isUnsigned: true, isBigEndian: false);
        
        // k is the bit length of D
        int k = 512;
        
        // Golden Master (Expected Result)
        BigInteger expectedReciprocal = (BigInteger.One << (2 * k)) / D;

        // 2. Initial Guess X_0
        // A simple initial guess using the highest 32 bits (or just a power of 2 approximation)
        // Since 2^{k-1} <= D < 2^k, we know 2^k < 2^(2k)/D <= 2^{k+1}
        // For simplicity in this test, we can start with a basic approximation
        BigInteger X = BigInteger.One << k;

        // 3. Newton-Raphson Iteration
        int maxIterations = 10;
        BigInteger twoTo2kPlus1 = BigInteger.One << (2 * k + 1);
        
        for (int i = 0; i < maxIterations; i++)
        {
            // X_{i+1} = X_i * (2^{2k+1} - D * X_i) / 2^{2k}
            BigInteger dTimesX = D * X;
            BigInteger term2 = twoTo2kPlus1 - dTimesX;
            BigInteger nextX = (X * term2) >> (2 * k);

            // Break if converged
            if (nextX == X || nextX == X + 1 || nextX == X - 1)
            {
                X = nextX;
                break;
            }
            X = nextX;
        }

        // Adjust possible off-by-one error typical in integer Newton-Raphson
        if (D * (X + 1) <= (BigInteger.One << (2 * k)))
        {
            X++;
        }
        else if (D * X > (BigInteger.One << (2 * k)))
        {
            X--;
        }

        // 4. Verify against Golden Master
        Assert.Equal(expectedReciprocal, X);
    }

    [SkippableFact]
    public void TestReciprocalMega_GpuExecution()
    {
        // Require actual GPU
        CudaCapability.EnsureGpuAvailable();
        using var _scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        int kBits = 512;
        int kWords = 16;
        var rng = new Random(42);
        byte[] dBytes = new byte[64];
        rng.NextBytes(dBytes);
        dBytes[63] |= 0x80;
        
        BigInteger D = new BigInteger(dBytes, isUnsigned: true, isBigEndian: false);
        BigInteger expectedReciprocal = (BigInteger.One << (2 * kBits)) / D;

        using var targetOperand = GpuBigUInt.FromBigInteger(workspace, D, kWords);

        // resultBuffer holds the initial guess, and will receive the final answer
        // size: same as D, but can be slightly larger depending on target bits
        int targetBits = 2 * kBits;
        int resultWords = (targetBits + 31) / 32;
        BigInteger initialGuess = BigInteger.One << kBits;
        using var resultBuffer = GpuBigUInt.FromBigInteger(workspace, initialGuess, resultWords);

        // Execute MegaKernel
        // Margin and bit counts as per standard NR integers
        int iterations = 10;
        
        // This is a complex internal API, ensure we supply correctly
        // (marginWords, numBits, marginBits, iterations, targetBits)
        var advancedMath = new KernelAdvancedMathHandler();
        advancedMath.ReciprocalMega(
            workspace, targetOperand, resultBuffer,
            marginWords: 2, 
            numBits: kBits, 
            marginBits: 0, 
            iterations: iterations, 
            targetBits: targetBits);

        // Retrieve result
        BigInteger actualReciprocal = resultBuffer.ToBigInteger();

        // ReciprocalMega corrects the Newton-Raphson result on the device, so it is exactly floor(2^(2k) / D).
        Assert.Equal(expectedReciprocal, actualReciprocal);
    }
}
