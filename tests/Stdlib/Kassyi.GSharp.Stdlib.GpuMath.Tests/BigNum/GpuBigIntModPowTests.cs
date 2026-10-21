
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntModPowTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ModPow_SmallValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        
        using var a = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        using var e = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        using var m = new GpuBigUInt(ctx, new uint[] { 5, 0, 0, 0 });
        
        using var result = new GpuBigUInt(ctx, 4);
        
        GpuBigIntDivRem.ModPow(a, e, m, result);
        
        Assert.Equal(2u, result.ToHostArray()[0]); // 3^3 mod 5 = 27 mod 5 = 2
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ModPow_LargeValues()
    {
        CudaCapability.EnsureGpuAvailable();
        const int NumWords = 64; // 2048 bits (> 1024 PolyAlgorithmThreshold -> ModPowZeroSyncController)
        using var ctx = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);

        var aArr = new uint[NumWords];
        var eArr = new uint[NumWords];
        var mArr = new uint[NumWords];
        Array.Fill(aArr, 0x11111111u);
        Array.Fill(mArr, 0x7FFFFFFFu);
        mArr[0] |= 1; // odd modulus

        // Multi-word exponent with an intermediate zero word and trailing zero words
        // to cover both word-skip continuation and early-exit branches in ModPowZeroSyncController.
        eArr[0] = 0x00000015u;
        eArr[1] = 0u;
        eArr[2] = 0x00000003u;

        using var a = new GpuBigUInt(ctx, aArr);
        using var e = new GpuBigUInt(ctx, eArr);
        using var m = new GpuBigUInt(ctx, mArr);
        using var result = new GpuBigUInt(ctx, NumWords);

        var expected = BigInteger.ModPow(a.ToBigInteger(), e.ToBigInteger(), m.ToBigInteger());

        GpuBigIntDivRem.ModPow(a, e, m, result);

        Assert.Equal(expected, result.ToBigInteger());
    }
}
