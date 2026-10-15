
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[Collection(TestCollections.GlobalStateSequential)]
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

    private static double ToDouble(GpuBigUInt sig, int exp, bool sign)
    {
        var val = (double)sig.ToBigInteger() * Math.Pow(2.0, exp);
        return sign ? -val : val;
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

        // Add: 4 + 2 = 6
        arith.Add(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out int resExp, out bool resSign);
        Assert.False(resSign);
        Assert.Equal(6.0, ToDouble(resSig, resExp, resSign), precision: 10);

        // Multiply: 4 * 2 = 8
        arith.Multiply(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
        Assert.Equal(8.0, ToDouble(resSig, resExp, resSign), precision: 10);

        // Divide: 4 / 2 = 2
        arith.Divide(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
        Assert.Equal(2.0, ToDouble(resSig, resExp, resSign), precision: 8);
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

        // Reciprocal: 1 / 4 = 0.25
        arith.Reciprocal(ref execContext, aHandle, 0, false, resHandle, out int resExp, out bool resSign);
        Assert.False(resSign);
        Assert.Equal(0.25, ToDouble(resSig, resExp, resSign), precision: 8);

        // Sqrt: sqrt(4) = 2.0
        arith.Sqrt(ref execContext, aHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
        Assert.Equal(2.0, ToDouble(resSig, resExp, resSign), precision: 8);

        // Rsqrt: 1 / sqrt(4) = 0.5
        arith.Rsqrt(ref execContext, aHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
        Assert.Equal(0.5, ToDouble(resSig, resExp, resSign), precision: 8);
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

        // Exp: exp(2)
        arith.Exp(ref execContext, aHandle, 0, false, resHandle, out int resExp, out bool resSign);
        Assert.False(resSign);
        Assert.Equal(Math.Exp(2.0), ToDouble(resSig, resExp, resSign), precision: 6);

        // Log: ln(2)
        arith.Log(ref execContext, aHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
        Assert.Equal(Math.Log(2.0), ToDouble(resSig, resExp, resSign), precision: 6);

        // Pow: 2^3 = 8
        arith.Pow(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
        Assert.Equal(8.0, ToDouble(resSig, resExp, resSign), precision: 6);
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
        using var divResSig = new GpuBigUInt(customCtx, 1);

        var aHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(aSig.DevicePointer, 1), 0, 1);
        var resHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resSig.DevicePointer, 4), 0, 4);
        var divResHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(divResSig.DevicePointer, 1), 0, 1);

        arith.Normalize(ref execContext, aHandle, 0, false, resHandle, out int resExp, out bool resSign);
        Assert.False(resSign);
        Assert.Equal(10.0, ToDouble(resSig, resExp, resSign), precision: 10);

        arith.DivideByUInt32(ref execContext, aHandle, 0, false, 3, divResHandle, out resExp, out resSign, out uint rem);
        Assert.False(resSign);
        Assert.Equal(0, resExp);
        Assert.Equal(3UL, (ulong)divResSig.ToBigInteger());
        Assert.Equal(1u, rem);
    }
}
