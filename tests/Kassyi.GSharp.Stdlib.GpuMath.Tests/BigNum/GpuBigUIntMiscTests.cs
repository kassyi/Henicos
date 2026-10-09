
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

public class GpuBigUIntMiscTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuBigUIntMiscTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }

    [Fact]
    public void Test_GpuBigUInt_Misc_Methods()
    {
        using var a = new GpuBigUInt(_context, 4);
        var hash = a.GetHashCode();
        Assert.NotEqual(0, hash);
        
        var str = a.ToString();
        Assert.Contains("GpuBigUInt", str);
        
        
        

        using var b = new GpuBigUInt(_context, 4);
        
        

        a.ClearWord(0);
        
        var proxy = a.ToProxy();
        Assert.NotEqual(IntPtr.Zero, proxy.Data);
        
        var val = a.Value;
        Assert.NotNull(val.Node);
    }
}
