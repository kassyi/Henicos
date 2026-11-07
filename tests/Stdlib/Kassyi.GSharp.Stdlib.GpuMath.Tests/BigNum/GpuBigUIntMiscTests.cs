
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

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(-1)]
    [InlineData(-33)]
    [InlineData(int.MinValue)]
    public void CreatePowerOfTwo_NegativeBitIndex_Throws(int bitIndex)
    {
        // A negative bit index has no power of two: -1 would shift by 31 (C# masks the count) and -33 would write the
        // word before the buffer.
        // mutant: GpuBigUInt.CreatePowerOfTwo `ThrowIfNegative(bitIndex)` removed (-1 returns 2^31, -33 throws CudaException)
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => GpuBigUInt.CreatePowerOfTwo(context, 2, bitIndex));
        Assert.Equal("bitIndex", error.ParamName);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(63)]
    [InlineData(64)]
    public void CreatePowerOfTwo_BitIndexFromZero_IsTwoToThePowerWithinTheWidth(int bitIndex)
    {
        // 2^bitIndex for a bit inside the 2 words, and zero for a bit past them.
        // mutant: GpuBigUInt.CreatePowerOfTwo `ThrowIfNegative` -> `ThrowIfNegativeOrZero` (bitIndex 0)
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);

        using var power = GpuBigUInt.CreatePowerOfTwo(context, 2, bitIndex);

        var expected = bitIndex < 64 ? BigInteger.One << bitIndex : BigInteger.Zero;
        Assert.Equal(expected, power.ToBigInteger());
    }
}
