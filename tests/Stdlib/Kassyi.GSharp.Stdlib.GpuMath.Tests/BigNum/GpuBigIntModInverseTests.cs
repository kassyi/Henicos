
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
}
