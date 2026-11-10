#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of CUDA graph capture of signed division (SignedDeviceSignTests covers the nodes whose sign the device
// writes):
// - /, % and the StackLinq Average (a division by the count) refuse the capture with NotSupportedException before the
//   read, instead of breaking the capture with a driver error; the workspace stays usable.

/// <summary>CUDA graph capture of signed /, % and Average.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class SignedCaptureTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public SignedCaptureTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    // Division reads the bit lengths of its operands, and % the zero test of a negative remainder; both refuse the
    // capture before reading, also for non-negative operands
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/SignedExpressions.cs SignedModExpression
    // and SignedDivExpression without `CaptureChecks.ThrowIfCapturing` (the read breaks the capture with a driver
    // error)
    [Theory]
    [InlineData(17)]
    [InlineData(-17)]
    [Scenario(TestScenario.SadPath)]
    public void Division_CannotBeCaptured(int dividend)
    {
        using var a = SignedCases.Value(_workspace, dividend, 2);
        using var b = SignedCases.Value(_workspace, 5, 2);

        Assert.Throws<NotSupportedException>(() => ((GpuBigIntExpr)a % b).CompileToGraph(_workspace));
        Assert.Throws<NotSupportedException>(() => ((GpuBigIntExpr)a / b).CompileToGraph(_workspace));

        // The workspace is still usable afterwards; C# % takes the sign of the dividend
        using var remainder = ((GpuBigIntExpr)a % b).Evaluate(_workspace);
        Assert.Equal(dividend % 5, SignedCases.Read(remainder));
    }

    // Average divides the sum by the count with DivRem, which reads the bit length of the sum; the operands are
    // non-negative, so the additions below it read nothing and only the division can refuse
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/SignedDivideByCountExpression.cs Execute
    // without `CaptureChecks.ThrowIfCapturing` (the read breaks the capture with a driver error)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Average_CannotBeCaptured()
    {
        using var a = SignedCases.Value(_workspace, 17, 2);
        using var b = SignedCases.Value(_workspace, 6, 2);
        GpuBigInt[] values = [a, b];

        using var average = values.AsStackEnumerable().AsParallel().Average();
        Assert.Throws<NotSupportedException>(() => average.CompileToGraph(_workspace));

        // The workspace is still usable afterwards
        using var result = average.Evaluate(_workspace);
        Assert.Equal(11, SignedCases.Read(result));
    }
}
