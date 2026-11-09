#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of CUDA graph capture of the signed nodes whose result depends on a value read from the device:
// - Max, Min, / and % refuse the capture with NotSupportedException before the read, instead of breaking the capture
//   with a driver error; the workspace stays usable.

/// <summary>CUDA graph capture of signed Max, Min, / and %.</summary>
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

    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/SignedExpressions.cs SignedSelect.Compare
    // without `SignedCapture.ThrowIfCapturing` (the capture fails with a driver error instead)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void MaxAndMin_CannotBeCaptured()
    {
        using var a = SignedCases.Value(_workspace, 5, 2);
        using var b = SignedCases.Value(_workspace, -7, 2);

        Assert.Throws<NotSupportedException>(() => GpuBigIntExpr.Max(a, b).CompileToGraph(_workspace));
        Assert.Throws<NotSupportedException>(() => GpuBigIntExpr.Min(a, b).CompileToGraph(_workspace));

        // The workspace is still usable afterwards
        using var max = GpuBigIntExpr.Max(a, b).Evaluate(_workspace);
        Assert.Equal(5, SignedCases.Read(max));
    }

    // Division reads the bit lengths of its operands, and % the zero test of a negative remainder; both refuse the
    // capture before reading, also for non-negative operands
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/SignedExpressions.cs SignedModExpression
    // and SignedDivExpression without `SignedCapture.ThrowIfCapturing` (the read breaks the capture with a driver
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
}
