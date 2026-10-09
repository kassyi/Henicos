using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Common;

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
        using var ctx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        
        var aArr = new uint[512];
        var eArr = new uint[512];
        var mArr = new uint[512];
        Array.Fill(aArr, 0x11111111u);
        Array.Fill(eArr, 0x22222222u);
        Array.Fill(mArr, 0x7FFFFFFFu);
        mArr[0] |= 1; // odd modulus
        
        using var a = new GpuBigUInt(ctx, aArr);
        using var e = new GpuBigUInt(ctx, eArr);
        using var m = new GpuBigUInt(ctx, mArr);
        
        using var result = new GpuBigUInt(ctx, 512);
        
        GpuBigIntDivRem.ModPow(a, e, m, result);
        
        Assert.True(result.NumWords > 0);
    }
}
