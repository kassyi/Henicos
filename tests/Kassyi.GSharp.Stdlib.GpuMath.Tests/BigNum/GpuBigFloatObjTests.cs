using GpuBigFloat = Kassyi.GSharp.Stdlib.GpuMath.BigNum.GpuBigFloat;
using GpuBigUInt = Kassyi.GSharp.Stdlib.GpuMath.BigNum.GpuBigUInt;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigFloatObjTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_BigFloat_Obj()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        
        using var sig = new GpuBigUInt(ctx, 4);
        using var a = new GpuBigFloat(sig, 4, false);
        
        Assert.Equal(4, a.UFloat.Significand.NumWords);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Test_IGpuBigNumber_UnifiedComparison_ZeroAlloc_And_Boundary()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(512, GpuExecutionEngine.GSharpAot);

        // 1. +0 == -0
        using var zeroMag1 = new GpuBigUInt(ctx, new uint[] { 0, 0 });
        using var zeroMag2 = new GpuBigUInt(ctx, new uint[] { 0 });
        using var posZero = new GpuBigFloat(zeroMag1, 10, false);
        using var negZero = new GpuBigFloat(zeroMag2, -5, true);
        Assert.Equal(0, posZero.CompareTo(negZero));
        Assert.True(posZero == negZero);

        // 2. Cross-word virtual shift comparison:
        // valA = 0x80000001 * 2^1 = 0x1_00000002 * 2^0
        using var magA = new GpuBigUInt(ctx, new uint[] { 0x80000001u });
        using var magB = new GpuBigUInt(ctx, new uint[] { 2u, 1u });
        using var magC = new GpuBigUInt(ctx, new uint[] { 3u, 1u });
        using var floatA = new GpuBigFloat(magA, 1, false);
        using var floatB = new GpuBigFloat(magB, 0, false);
        using var floatC = new GpuBigFloat(magC, 0, false);

        Assert.Equal(0, floatA.CompareTo(floatB));
        Assert.True(floatA == floatB);
        Assert.True(floatA < floatC);
        Assert.True(floatC > floatA);

        // 3. Negative comparisons
        using var negA = new GpuBigFloat(magA, 1, true);
        using var negC = new GpuBigFloat(magC, 0, true);
        Assert.True(negC < negA);
        Assert.True(negA > negC);

        // 4. Generic IGpuBigNumber<T> contract check across all 4 arbitrary-precision types
        static int GenericCompare<T>(T x, T y) where T : struct, IGpuBigNumber<T>
            => GpuBigNumberAlgorithms.Compare(x, y);

        Assert.Equal(0, GenericCompare(floatA, floatB));
        Assert.True(GenericCompare(magA, magB) < 0);

        // 5. Fixed-width IGpuNumber<TSelf> contract check
        GpuUInt128 u128 = [0u, 0u, 1u, 0u];
        Assert.False(u128.IsZero());
        Assert.False(u128.IsNegative);
        Assert.False(u128.CanBeNegative);
        Assert.Equal(65, u128.GetBitLength());
    }
}
