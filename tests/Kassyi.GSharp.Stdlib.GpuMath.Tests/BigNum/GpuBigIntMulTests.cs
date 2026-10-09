using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Common;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntMulTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Mul_SmallValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        
        using var a = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        using var b = new GpuBigUInt(ctx, new uint[] { 4, 0, 0, 0 });
        
        using var result = new GpuBigUInt(ctx, 8);
        
        GpuBigIntMath.Mul(a, b, result);
        
        Assert.Equal(12u, result.ToHostArray()[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Mul_LargeValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        
        var aArr = new uint[512];
        var bArr = new uint[512];
        Array.Fill(aArr, 0xFFFFFFFFu);
        Array.Fill(bArr, 0xFFFFFFFFu);
        
        using var a = new GpuBigUInt(ctx, aArr);
        using var b = new GpuBigUInt(ctx, bArr);
        
        using var result = new GpuBigUInt(ctx, 1024);
        
        GpuBigIntMath.Mul(a, b, result);
        
        Assert.True(result.NumWords > 0);
    }
}
