using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Common;
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
}
