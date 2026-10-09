using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;
using Kassyi.GSharp.Tests.Common;
using Xunit;

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
        
        // These rely on implicit operators and Evaluate!
        GpuBigUFloat result1 = expr1;
        GpuBigUFloat result2 = expr2;
        
        result1.Dispose();
        result2.Dispose();
    }

    [SkippableFact]
    public void Test_NotImplementedMethods()
    {
        CudaCapability.EnsureGpuAvailable();

        using var aSig = new GpuBigUInt(_context, new uint[] { 4 });
        using var a = new GpuBigUFloat(aSig, 0);

        Assert.Throws<NotImplementedException>(() => a.AlignRadixPoint(a));
        Assert.Throws<NotImplementedException>(() => a.Round(GpuBigFloatRoundingMode.RoundToNearestEven, false, false, false));
        Assert.Throws<NotImplementedException>(() => a.Add(a));
        Assert.Throws<NotImplementedException>(() => a.Subtract(a));
        Assert.Throws<NotImplementedException>(() => a.CompareTo(a));
        Assert.Throws<NotImplementedException>(() => a.Equals(a));
        Assert.Throws<NotImplementedException>(() => a.Equals((object)a));
        
        // This will call CompareTo and throw
#pragma warning disable CS1718
        Assert.Throws<NotImplementedException>(() => a < a);
        Assert.Throws<NotImplementedException>(() => a > a);
        Assert.Throws<NotImplementedException>(() => a <= a);
        Assert.Throws<NotImplementedException>(() => a >= a);
        Assert.Throws<NotImplementedException>(() => a == a);
        Assert.Throws<NotImplementedException>(() => a != a);
#pragma warning restore CS1718
    }
}
