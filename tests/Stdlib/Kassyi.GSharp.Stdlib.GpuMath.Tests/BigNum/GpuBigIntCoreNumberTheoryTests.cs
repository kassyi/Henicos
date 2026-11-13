using static Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.GpuBigIntCoreTestHelpers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class GpuBigIntCoreNumberTheoryTests
{
    public GpuBigIntCoreNumberTheoryTests()
    {
        CudaCapability.EnsureGpuAvailable();
        // CUDA context is initialized automatically upon first kernel launch if supported.
        // Do not initialize here to prevent xUnit discovery crashes on non-GPU CI runners.
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    public void Gcd_ParallelLargeDigits_MatchesBigIntegerReference()
    {
        CudaCapability.EnsureGpuAvailable();
        // Issue #112 & #117: Test large GCD to trigger pipeline stalls / TDR issues.
        using var ctx = new GpuWorkspace(128, GpuExecutionEngine.GSharpAot);

        var arrA = GenerateRandomArray(128, 42); // 4096-bit number
        var arrB = GenerateRandomArray(128, 43);

        var bigA = ToBigInteger(arrA);
        var bigB = ToBigInteger(arrB);
        var expected = BigInteger.GreatestCommonDivisor(bigA, bigB);

        using var gpuA = new GpuBigUInt(ctx, arrA);
        using var gpuB = new GpuBigUInt(ctx, arrB);
        using var result = new GpuBigUInt(ctx, 128);

        GpuBigIntMath.Gcd(gpuA, gpuB, result);
        Assert.Equal(expected, ToBigInteger(result.ToHostArray()));
    }

    [SkippableTheory]
    // Standard prime: p = 17, threshold = primeModulus - b = 7 (for b = 10)
    [InlineData(6u, 10u, 17u)] // Just below threshold: (6 + 10 = 16 < 17) -> 16
    [InlineData(7u, 10u, 17u)] // Exactly at threshold: (7 + 10 = 17) -> 0
    [InlineData(8u, 10u, 17u)] // Just above threshold: (8 + 10 = 18 > 17) -> 1
    [InlineData(5u, 10u, 17u)] // Normal no-wrap
    [InlineData(12u, 10u, 17u)] // Normal wrap
    [InlineData(0u, 0u, 17u)] // Zero
    [InlineData(16u, 0u, 17u)] // Just below prime
    [InlineData(0u, 16u, 17u)] // Just below prime
    [InlineData(16u, 1u, 17u)] // Exactly prime (wraps to 0)
    [InlineData(16u, 16u, 17u)] // Max valid residues for p=17 -> 15
    // Near uint.MaxValue prime: p = 4294967295u, threshold = 4294967293u (for b = 2u)
    [InlineData(4294967292u, 2u, 4294967295u)] // Just below threshold -> 4294967294
    [InlineData(4294967293u, 2u, 4294967295u)] // Exactly at threshold -> 0
    [InlineData(4294967294u, 2u, 4294967295u)] // Just above threshold -> 1
    [InlineData(0u, 4294967294u, 4294967295u)]
    [InlineData(4294967294u, 4294967294u, 4294967295u)] // Max valid residues -> 4294967293
    [InlineData(4294967293u, 1u, 4294967295u)]
    [InlineData(4294967294u, 1u, 4294967295u)] // Wraps to 0
    public void ModAdd_ExtremaConditions_MatchesModularSum(uint a, uint b, uint p)
    {
        CudaCapability.EnsureGpuAvailable();
        var expected = (uint)(((ulong)a + b) % p);
        var actual = NttPrimeArithmetic.ModAdd(a, b, p);
        Assert.Equal(expected, actual);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Gcd_RandomLargeOperands_MatchesBigIntegerGcd(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);

        // Generate two random big integers
        var arrA = GenerateRandomArray(TestSizeWords, 50);
        var arrB = GenerateRandomArray(TestSizeWords, 51);

        var bigA = ToBigInteger(arrA);
        var bigB = ToBigInteger(arrB);

        // Calculate expected GCD using System.Numerics.BigInteger
        var expectedGcd = BigInteger.GreatestCommonDivisor(bigA, bigB);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        GpuBigIntMath.Gcd(gpuA, gpuB, gpuResult);

        var actualGcd = ToBigInteger(gpuResult.ToHostArray());

        Assert.Equal(expectedGcd, actualGcd);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Gcd_EmptyCollection_ThrowsException(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(10, engine);
        var arrEmpty = Array.Empty<uint>();

        Assert.ThrowsAny<Exception>(() =>
        {
            using var gpuA = new GpuBigUInt(context, arrEmpty);
            using var gpuB = new GpuBigUInt(context, arrEmpty);
            using var gpuResult = new GpuBigUInt(context, 0);

            GpuBigIntMath.Gcd(gpuA, gpuB, gpuResult);
        });
    }

    [SkippableTheory]
    // Happy path
    [InlineData(GpuExecutionEngine.GSharpAot, 48u, 18u)]
    // Edge cases: 0
    [InlineData(GpuExecutionEngine.GSharpAot, 0u, 0u)]
    [InlineData(GpuExecutionEngine.GSharpAot, 0u, 10u)]
    [InlineData(GpuExecutionEngine.GSharpAot, 10u, 0u)]
    // Edge cases: 1
    [InlineData(GpuExecutionEngine.GSharpAot, 1u, 1u)]
    [InlineData(GpuExecutionEngine.GSharpAot, 1u, 100u)]
    [InlineData(GpuExecutionEngine.GSharpAot, 100u, 1u)]
    // Edge cases: MaxValue and Alternating Bits
    [InlineData(GpuExecutionEngine.GSharpAot, uint.MaxValue, uint.MaxValue)]
    [InlineData(GpuExecutionEngine.GSharpAot, uint.MaxValue, 1u)]
    [InlineData(GpuExecutionEngine.GSharpAot, 0x80000000u, 0x55555555u)]
    public void Gcd_SingleWordOperands_ReturnsExpectedGcd(GpuExecutionEngine engine, uint valA, uint valB)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(10, engine);
        var arrA = new uint[] { valA };
        var arrB = new uint[] { valB };

        var bigA = ToBigInteger(arrA);
        var bigB = ToBigInteger(arrB);
        var expectedGcd = BigInteger.GreatestCommonDivisor(bigA, bigB);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);
        using var gpuResult = new GpuBigUInt(context, 1);

        GpuBigIntMath.Gcd(gpuA, gpuB, gpuResult);

        var actualGcd = ToBigInteger(gpuResult.ToHostArray());
        Assert.Equal(expectedGcd, actualGcd);
    }

    [SkippableTheory]
    [InlineData(0u, 0u, 4253024257u, 0u)]
    [InlineData(100u, 200u, 4253024257u, 300u)]
    [InlineData(4253024256u, 1u, 4253024257u, 0u)]
    [InlineData(4253024256u, 2u, 4253024257u, 1u)]
    [InlineData(0x80000000u, 0x80000000u, 4253024257u, 41943039u)] // 2^32 % 4253024257 = 41943039
    [InlineData(4293918720u, 4293918720u, 4293918721u, 4293918719u)] // Near uint.MaxValue prime
    [InlineData(0xC0000000u, 0xC0000000u, 4293918721u, 2148532223u)] // 6442450944 - 4293918721 = 2148532223
    public void ModAdd_OverflowOperands_MatchesModularSum(uint a, uint b, uint prime, uint expected)
    {
        CudaCapability.EnsureGpuAvailable();
        var actual = NttPrimeArithmetic.ModAdd(a, b, prime);
        Assert.Equal(expected, actual);
    }

    [SkippableFact]
    public void NttCarryResolver_ZeroLengthBuffers_RunWithNoElement()
    {
        CudaCapability.EnsureGpuAvailable();
        var low = Array.Empty<uint>();
        var high = Array.Empty<uint>();
        var output = Array.Empty<uint>();
        var blockCarries = Array.Empty<uint>();

        // The launchers pass empty array arguments as they are, as a C# call does; the kernels read no element
        GpuBigIntNttKernels.LaunchResolveNttCarriesBlockedKernel(low, high, output, blockCarries, 0, 0, 256);
        GpuBigIntNttKernels.LaunchResolveNttCarriesApplyKernel(output, blockCarries, 0, 256, 0);

        Assert.Empty(output);
    }

    [SkippableFact]
    public void NttCarryResolver_SingleWordBuffer_ComputesExpectedCarry()
    {
        CudaCapability.EnsureGpuAvailable();
        var low = new uint[] { 0x12345678, 0x9ABCDEF0 };
        var high = new uint[] { 0, 0 };
        var output = new uint[1];
        var blockCarries = new uint[1];

        GpuBigIntNttKernels.LaunchResolveNttCarriesBlockedKernel(low, high, output, blockCarries, 2, 1,
            256);
        GpuBigIntNttKernels.LaunchResolveNttCarriesApplyKernel(output, blockCarries, 1, 256, 1);

        // Calculation logic:
        // sum0 = 0x12345678 -> low16 = 0x5678, carry = 0x1234
        // sum1 = 0x1234 + 0x9ABCDEF0 = 0x9ABCF124 -> high16 = 0xF124, carry = 0x9ABC
        // output[0] = low16 | (high16 << 16) = 0x5678 | (0xF124 << 16) = 0xF1245678
        Assert.Equal(0xF1245678, output[0]);
    }
}
