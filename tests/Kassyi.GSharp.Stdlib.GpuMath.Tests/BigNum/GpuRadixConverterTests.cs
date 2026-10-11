
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.CpuExecution)]
public class GpuRadixConverterTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuRadixConverterTests()
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

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ConvertAndWrite()
    {
        CudaCapability.EnsureGpuAvailable();

        using var value = new GpuBigUInt(_context, new uint[] { 12345 });
        using var sw = new StringWriter();

        GpuRadixConverter.ConvertAndWrite(_context, value, 5, sw, true);
        
        var output = sw.ToString();
        Assert.Equal("1.2345", output);
    }

    [SkippableFact]
    public void Test_ConvertAndWrite_Large()
    {
        CudaCapability.EnsureGpuAvailable();

        using var value = new GpuBigUInt(_context, new uint[] { 1 });
        using var sw = new StringWriter();

        // 10001 digits, which forces the GPU Divide & Conquer loop
        GpuRadixConverter.ConvertAndWrite(_context, value, 10001, sw, false);
        
        var output = sw.ToString();
        Assert.Equal(10001, output.Length);
        Assert.EndsWith("1", output);
        Assert.StartsWith("00000", output);
    }
}
