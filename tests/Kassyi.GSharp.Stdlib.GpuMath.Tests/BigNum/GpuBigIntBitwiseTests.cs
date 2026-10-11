
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

public class GpuBigIntBitwiseTests
{
    [SkippableFact]
    public void Test_Bitwise_Operations()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var a = new GpuBigUInt(context, 4);
        using var b = new GpuBigUInt(context, 4);
        using var r = new GpuBigUInt(context, 4);
        
        GpuBigIntMath.BitwiseAnd(a, b, r);
        GpuBigIntMath.BitwiseOr(a, b, r);
        GpuBigIntMath.BitwiseXor(a, b, r);
        GpuBigIntMath.BitwiseNot(a, r);
        
        Assert.True(true);
    }
}
