using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Common;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntDivRemTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DivRem_SmallValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        
        using var a = new GpuBigUInt(ctx, new uint[] { 1000, 0, 0, 0 });
        using var b = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        
        using var divResult = new GpuBigUInt(ctx, 4);
        using var remResult = new GpuBigUInt(ctx, 4);
        
        GpuBigIntDivRem.DivRem(a, b, divResult, remResult);
        
        Assert.Equal(333u, divResult.ToHostArray()[0]);
        Assert.Equal(1u, remResult.ToHostArray()[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DivRem_LargeValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(512, GpuExecutionEngine.GSharpAot);
        
        var aArr = new uint[512];
        var bArr = new uint[512];
        Array.Fill(aArr, 0xFFFFFFFFu);
        Array.Fill(bArr, 0x12345678u);
        bArr[256] = 0; // Make B smaller
        
        using var a = new GpuBigUInt(ctx, aArr);
        using var b = new GpuBigUInt(ctx, bArr);
        
        using var divResult = new GpuBigUInt(ctx, 512);
        using var remResult = new GpuBigUInt(ctx, 512);
        
        GpuBigIntDivRem.DivRem(a, b, divResult, remResult);
        
        Assert.True(divResult.NumWords > 0);
    }
}
