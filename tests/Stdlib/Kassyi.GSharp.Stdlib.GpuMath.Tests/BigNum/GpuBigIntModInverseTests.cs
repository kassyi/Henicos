
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntModInverseTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ModInverse()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        
        using var a = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        using var m = new GpuBigUInt(ctx, new uint[] { 11, 0, 0, 0 });
        using var result = new GpuBigUInt(ctx, 4);
        
        GpuBigIntMath.ModInverse(a, m, result);
        
        Assert.Equal(4u, result.ToHostArray()[0]); // 3 * 4 = 12 = 1 (mod 11)
    }

    public static TheoryData<string, string> EvenModulusCases => new()
    {
        { "17", "3120" },
        { "3", "10" },
        // 4-word odd value and the even modulus 2^127, coprime to any odd value.
        { "123456789012345678901234567890123456789", "170141183460469231731687303715884105728" },
    };

    // An even modulus (odd value) goes through the host-orchestrated Euclidean loop; the result is still the inverse:
    // 0 <= r < n and (a * r) mod n == 1.
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(EvenModulusCases))]
    public void ModInverse_EvenModulus_MatchesReference(string aText, string nText)
    {
        CudaCapability.EnsureGpuAvailable();
        var a = BigInteger.Parse(aText);
        var n = BigInteger.Parse(nText);
        Assert.Equal(BigInteger.One, BigInteger.GreatestCommonDivisor(a, n));

        using var ctx = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        using var gpuA = GpuBigUInt.FromBigInteger(ctx, a, 4);
        using var gpuN = GpuBigUInt.FromBigInteger(ctx, n, 4);
        using var result = new GpuBigUInt(ctx, 4);

        // mutant: KernelAdvancedMathHandler.cs:119 host loop call removed, L120 `return` removed
        // (the binary GCD kernel requires an odd modulus)
        GpuBigIntMath.ModInverse(gpuA, gpuN, result);

        var r = result.ToBigInteger();
        Assert.True(r < n);
        Assert.Equal(BigInteger.One, a * r % n);
    }

    // A one-word operand converges within the iteration limit of the binary GCD kernel.
    // n = 2^32 - 5 is prime and a = n - 1 = -1 (mod n), whose inverse is itself.
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void ModInverse_SingleWordNearMax_Converges()
    {
        CudaCapability.EnsureGpuAvailable();
        const uint N = 4294967291u;
        const uint A = N - 1;
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        using var gpuA = new GpuBigUInt(ctx, [A]);
        using var gpuN = new GpuBigUInt(ctx, [N]);
        using var result = new GpuBigUInt(ctx, 1);

        // mutant: KernelAdvancedMathHandler.cs:138 `numWords * 32 * 4 + 100` -> `- 100` (228 -> 28 iterations)
        GpuBigIntMath.ModInverse(gpuA, gpuN, result);

        var r = result.ToBigInteger();
        Assert.Equal(new BigInteger(A), r);
        Assert.Equal(BigInteger.One, (BigInteger)A * r % N);
    }
}
