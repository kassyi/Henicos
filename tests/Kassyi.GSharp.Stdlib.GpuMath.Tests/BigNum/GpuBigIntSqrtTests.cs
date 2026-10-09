using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Common;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntSqrtTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Sqrt_SmallValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        
        using var a = new GpuBigUInt(ctx, new uint[] { 10000, 0, 0, 0 });
        using var result = new GpuBigUInt(ctx, 4);
        
        GpuBigIntDivRem.Sqrt(a, result);
        
        Assert.Equal(100u, result.ToHostArray()[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Sqrt_LargeValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(512, GpuExecutionEngine.GSharpAot);
        
        var aArr = new uint[512];
        Array.Fill(aArr, 0xFFFFFFFFu);
        
        using var a = new GpuBigUInt(ctx, aArr);
        using var result = new GpuBigUInt(ctx, 512);
        
        GpuBigIntDivRem.Sqrt(a, result);
        
        Assert.True(result.NumWords > 0);
    }
}
