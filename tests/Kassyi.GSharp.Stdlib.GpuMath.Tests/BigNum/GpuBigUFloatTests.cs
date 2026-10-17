
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.CpuExecution)]
public class GpuBigUFloatTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuBigUFloatTests()
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
    public void Test_Normalize_And_Divide()
    {
        CudaCapability.EnsureGpuAvailable();

        using var sig = new GpuBigUInt(_context, new uint[] { 10 });
        using var a = new GpuBigUFloat(sig, 0);

        using var normalized = a.Normalize(0, false);
        Assert.Equal(0, normalized.Exponent);

        var (q, r) = a.DivideByUInt32(2);
        q.Dispose();
    }

    [SkippableFact]
    public void Test_Expressions()
    {
        CudaCapability.EnsureGpuAvailable();

        using var aSig = new GpuBigUInt(_context, new uint[] { 4 });
        using var bSig = new GpuBigUInt(_context, new uint[] { 2 });
        using var a = new GpuBigUFloat(aSig, 0);
        using var b = new GpuBigUFloat(bSig, 0);

        var expr1 = a + b;
        var expr2 = a - b;
        var expr3 = a * b;
        var expr4 = a / b;
        
        // These rely on implicit operators and Evaluate!
        GpuBigUFloat result1 = expr1;
        GpuBigUFloat result2 = expr2;
        GpuBigUFloat result3 = expr3;
        GpuBigUFloat result4 = expr4;
        
        result1.Dispose();
        result2.Dispose();
        result3.Dispose();
        result4.Dispose();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DirectArithmetic_And_Comparison()
    {
        CudaCapability.EnsureGpuAvailable();

        using var aSig = new GpuBigUInt(_context, new uint[] { 12 });
        using var bSig = new GpuBigUInt(_context, new uint[] { 3 });
        using var cSig = new GpuBigUInt(_context, new uint[] { 3 });
        using var a = new GpuBigUFloat(aSig, 0);
        using var b = new GpuBigUFloat(bSig, 0);
        using var c = new GpuBigUFloat(cSig, 2); // 3 * 2^2 = 12

        using var sum = a.Add(b);
        using var diff = a.Subtract(b);
        using var prod = a.Multiply(b);
        using var quot = a.Divide(b);

        double ToDouble(GpuBigUFloat v) => (double)v.Significand.ToBigInteger() * Math.Pow(2.0, v.Exponent);

        Assert.Equal(15.0, ToDouble(sum), precision: 8);
        Assert.Equal(9.0, ToDouble(diff), precision: 8);
        Assert.Equal(36.0, ToDouble(prod), precision: 8);
        Assert.Equal(4.0, ToDouble(quot), precision: 8);

        // Zero-allocation CompareTo across different exponents (12 * 2^0 == 3 * 2^2)
        Assert.Equal(0, a.CompareTo(c));
        Assert.True(a.Equals(c));
        Assert.True(a.Equals((object)c));
        Assert.True(a == c);
        Assert.False(a != c);
        Assert.True(a <= c);
        Assert.True(a >= c);

        Assert.True(a > b);
        Assert.True(b < a);
        Assert.False(a.Equals(b));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_NotImplementedMethods()
    {
        CudaCapability.EnsureGpuAvailable();

        using var aSig = new GpuBigUInt(_context, new uint[] { 4 });
        using var a = new GpuBigUFloat(aSig, 0);

        Assert.Throws<NotImplementedException>(() => a.AlignRadixPoint(a));
        Assert.Throws<NotImplementedException>(() => a.Round(GpuBigFloatRoundingMode.RoundToNearestEven, false, false, false));
    }
}
