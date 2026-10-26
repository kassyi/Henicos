
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[Category(TestCategory.Stdlib)]
public class GpuBigUIntMiscTests
{
    [SkippableFact]
    public void Test_GpuBigUInt_Misc_Methods()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var a = new GpuBigUInt(context, 4);
        var hash = a.GetHashCode();
        Assert.NotEqual(0, hash);
        
        var str = a.ToString();
        Assert.Contains("GpuBigUInt", str);
        
        using var b = new GpuBigUInt(context, 4);
        
        a.ClearWord(0);
        
        var proxy = a.ToProxy();
        Assert.NotEqual(IntPtr.Zero, proxy.Data);
        
        var val = a.Value;
        Assert.NotNull(val.Node);
    }
}
