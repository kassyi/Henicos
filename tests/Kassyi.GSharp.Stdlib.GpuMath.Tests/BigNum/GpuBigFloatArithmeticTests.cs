using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Common;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigFloatArithmeticTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_BigFloat_Add_Large()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(512, GpuExecutionEngine.GSharpAot);
        
        var aArr = new uint[128];
        var bArr = new uint[128];
        Array.Fill(aArr, 0xFFFFFFFFu);
        Array.Fill(bArr, 0x11111111u);
        
        using var aSig = new GpuBigUInt(ctx, aArr);
        using var bSig = new GpuBigUInt(ctx, bArr);
        
        using var a = new GpuBigFloat(aSig, 100, false);
        using var b = new GpuBigFloat(bSig, 100, false);
        
        var visitor = new GpuBigFloatExpressionVisitor(ctx);
        var expr = a + b;
        using var result = visitor.Visit(expr);
        
        Assert.True(result.UFloat.Significand.NumWords > 0);
    }
}
