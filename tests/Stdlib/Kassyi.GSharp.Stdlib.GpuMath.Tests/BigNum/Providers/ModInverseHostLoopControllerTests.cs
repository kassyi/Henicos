using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

[TestTarget(ExecutionTarget.CpuExecution)]
public class ModInverseHostLoopControllerTests : IDisposable
{
    private readonly GpuWorkspace _workspace;

    public ModInverseHostLoopControllerTests()
    {
        // For testing the controller logic itself, CPU emulation is sufficient and stable.
        GpuKernelLoader.ForceCpuFallback = true;
        _workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        GpuKernelLoader.ForceCpuFallback = false;
    }

    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(3, 11, 4)]
    [InlineData(17, 3120, 2753)]
    [InlineData(42, 2017, 1969)]
    public void Execute_ReturnsCorrectModInverse(ulong value, ulong modulus, ulong expectedResult)
    {
        // Arrange
        using var a = new GpuBigUInt(_workspace, new uint[] { (uint)value, (uint)(value >> 32), 0, 0 });
        using var m = new GpuBigUInt(_workspace, new uint[] { (uint)modulus, (uint)(modulus >> 32), 0, 0 });
        using var res = new GpuBigUInt(_workspace, 4);

        res.Clear();

        // Act
        ModInverseHostLoopController.Execute(_workspace, a, m, res);

        // Assert
        var actual = res.ToBigInteger();
        Assert.Equal(new BigInteger(expectedResult), actual);
    }

    // Modulus 1: every residue is 0, so the inverse is 0 (as in CSharpCpuBigIntProvider.ModInverse). The public path
    // (KernelAdvancedMathHandler.ModInverse) sends odd moduli, 1 included, to the binary GCD kernel and calls this
    // controller only for even moduli, so the controller is called directly here.
    // Value 1 ends the loop after one step with the starting d = 1 as the result; values 0 and 3 do not reach that.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/ModInverseHostLoopController.cs:21 delete `return;` after `resultBuffer.Clear();`
    // input: value 1
    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Execute_ModulusOne_ReturnsZero(ulong value)
    {
        using var a = new GpuBigUInt(_workspace, new uint[] { (uint)value, (uint)(value >> 32), 0, 0 });
        using var m = new GpuBigUInt(_workspace, new uint[] { 1, 0, 0, 0 });
        // A non-zero result buffer, so a result that is never written is not mistaken for 0.
        using var res = new GpuBigUInt(_workspace, new uint[] { 0xDEADBEEF, 0, 0, 0 });

        ModInverseHostLoopController.Execute(_workspace, a, m, res);

        Assert.Equal(BigInteger.Zero, res.ToBigInteger());
    }

    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(15, 3120)]
    [InlineData(2, 4)]
    [InlineData(10, 100)]
    public void Execute_ThrowsArithmeticException_WhenNotCoprime(ulong value, ulong modulus)
    {
        // Arrange
        using var a = new GpuBigUInt(_workspace, new uint[] { (uint)value, (uint)(value >> 32), 0, 0 });
        using var m = new GpuBigUInt(_workspace, new uint[] { (uint)modulus, (uint)(modulus >> 32), 0, 0 });
        using var res = new GpuBigUInt(_workspace, 4);

        res.Clear();

        // Act & Assert
        var exception = Assert.Throws<ArithmeticException>(() => ModInverseHostLoopController.Execute(_workspace, a, m, res));
        Assert.Contains("coprime", exception.Message);
    }
}
