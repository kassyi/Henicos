using System;
using System.Numerics;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;
using Kassyi.GSharp.Tests.Common;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

[TestTarget(ExecutionTarget.CpuExecution)]
public class ModInverseHostLoopControllerTests : IDisposable
{
    private readonly GpuWorkspace _workspace;

    public ModInverseHostLoopControllerTests()
    {
        // For testing the controller logic itself, CPU emulation is sufficient and stable.
        Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.ForceCpuFallback = true;
        _workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.ForceCpuFallback = false;
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
        Assert.Equal(new System.Numerics.BigInteger(expectedResult), actual);
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
