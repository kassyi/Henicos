
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

public class GSharpAotFloatProviderTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;

    public GSharpAotFloatProviderTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Arithmetic_Add_Multiply_Divide(bool useNative)
    {
        using var customCtx = new GpuWorkspace(1024, useNative ? GpuExecutionEngine.LegacyNative : GpuExecutionEngine.GSharpAot);
        var execContext = new GpuBigIntExecutionContext(customCtx, customCtx.MemoryManager, new LegacyProviderArithmeticAdapter(), customCtx.Stream);
        var arith = useNative ? GSharpAotFloatProvider.CreateNativeCuda() : GSharpAotFloatProvider.CreateGSharpKernel();

        using var aSig = new GpuBigUInt(customCtx, new uint[] { 4 });
        using var bSig = new GpuBigUInt(customCtx, new uint[] { 2 });
        using var resSig = new GpuBigUInt(customCtx, 4);

        var aHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(aSig.DevicePointer, 1), 0, 1);
        var bHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(bSig.DevicePointer, 1), 0, 1);
        var resHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resSig.DevicePointer, 4), 0, 4);

        // Add
        arith.Add(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out int resExp, out bool resSign);
        Assert.False(resSign);

        // Multiply
        arith.Multiply(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);

        // Divide
        arith.Divide(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
    }

    [SkippableTheory]
    [InlineData(false)]
    public void Test_Arithmetic_Reciprocal_Sqrt_Rsqrt(bool useNative)
    {
        using var customCtx = new GpuWorkspace(1024, useNative ? GpuExecutionEngine.LegacyNative : GpuExecutionEngine.GSharpAot);
        var execContext = new GpuBigIntExecutionContext(customCtx, customCtx.MemoryManager, new LegacyProviderArithmeticAdapter(), customCtx.Stream);
        var arith = useNative ? GSharpAotFloatProvider.CreateNativeCuda() : GSharpAotFloatProvider.CreateGSharpKernel();

        using var aSig = new GpuBigUInt(customCtx, new uint[] { 4 });
        using var resSig = new GpuBigUInt(customCtx, 4);

        var aHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(aSig.DevicePointer, 1), 0, 1);
        var resHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resSig.DevicePointer, 4), 0, 4);

        // Reciprocal
        arith.Reciprocal(ref execContext, aHandle, 0, false, resHandle, out int resExp, out bool resSign);

        // Sqrt
        arith.Sqrt(ref execContext, aHandle, 0, false, resHandle, out resExp, out resSign);

        // Rsqrt
        arith.Rsqrt(ref execContext, aHandle, 0, false, resHandle, out resExp, out resSign);
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Test_Arithmetic_Exp_Log_Pow(bool useNative)
    {
        using var customCtx = new GpuWorkspace(1024, useNative ? GpuExecutionEngine.LegacyNative : GpuExecutionEngine.GSharpAot);
        var execContext = new GpuBigIntExecutionContext(customCtx, customCtx.MemoryManager, new LegacyProviderArithmeticAdapter(), customCtx.Stream);
        var arith = useNative ? GSharpAotFloatProvider.CreateNativeCuda() : GSharpAotFloatProvider.CreateGSharpKernel();

        using var aSig = new GpuBigUInt(customCtx, new uint[] { 2 });
        using var bSig = new GpuBigUInt(customCtx, new uint[] { 3 });
        using var resSig = new GpuBigUInt(customCtx, 4);

        var aHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(aSig.DevicePointer, 1), 0, 1);
        var bHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(bSig.DevicePointer, 1), 0, 1);
        var resHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resSig.DevicePointer, 4), 0, 4);

        // Exp
        arith.Exp(ref execContext, aHandle, 0, false, resHandle, out int resExp, out bool resSign);

        // Log
        arith.Log(ref execContext, aHandle, 0, false, resHandle, out resExp, out resSign);

        // Pow
        arith.Pow(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out resExp, out resSign);
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Test_Arithmetic_Normalize_DivideByUInt32(bool useNative)
    {
        using var customCtx = new GpuWorkspace(1024, useNative ? GpuExecutionEngine.LegacyNative : GpuExecutionEngine.GSharpAot);
        var execContext = new GpuBigIntExecutionContext(customCtx, customCtx.MemoryManager, new LegacyProviderArithmeticAdapter(), customCtx.Stream);
        var arith = useNative ? GSharpAotFloatProvider.CreateNativeCuda() : GSharpAotFloatProvider.CreateGSharpKernel();

        using var aSig = new GpuBigUInt(customCtx, new uint[] { 10 });
        using var resSig = new GpuBigUInt(customCtx, 4);

        var aHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(aSig.DevicePointer, 1), 0, 1);
        var resHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resSig.DevicePointer, 4), 0, 4);

        arith.Normalize(ref execContext, aHandle, 0, false, resHandle, out int resExp, out bool resSign);
        
        arith.DivideByUInt32(ref execContext, aHandle, 0, false, 2, resHandle, out resExp, out resSign, out uint rem);
    }
}
