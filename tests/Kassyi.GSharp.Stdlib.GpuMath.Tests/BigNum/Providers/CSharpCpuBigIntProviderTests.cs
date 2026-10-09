using System;
using System.Numerics;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
using Kassyi.GSharp.Tests.Common;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

[TestTarget(ExecutionTarget.CpuExecution)]
public class CSharpCpuBigIntProviderTests : IDisposable
{
    private readonly GpuWorkspace _workspace;
    private readonly IGpuExecutionProvider _provider;

    public CSharpCpuBigIntProviderTests()
    {
        Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.ForceCpuFallback = true;
        _workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        _provider = CSharpCpuBigIntProvider.Instance;
    }

    public void Dispose()
    {
        _workspace.Dispose();
        Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.ForceCpuFallback = false;
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void MemoryOperations_WorkCorrectly()
    {
        IntPtr ptr = _provider.AllocateMemory(4);
        Assert.NotEqual(IntPtr.Zero, ptr);
        _provider.ClearMemoryAsync(ptr, 4, IntPtr.Zero);
        
        _provider.WriteWord(ptr, 0, 12345);
        _provider.WriteWord(ptr, 3, 67890);
        Assert.Equal(12345u, _provider.ReadWord(ptr, 0));
        Assert.Equal(0u, _provider.ReadWord(ptr, 1));
        Assert.Equal(67890u, _provider.ReadWord(ptr, 3));

        uint[] hostData = new uint[] { 1, 2, 3, 4 };
        _provider.CopyHostToDevice(ptr, hostData, 4);
        Assert.Equal(1u, _provider.ReadWord(ptr, 0));
        Assert.Equal(4u, _provider.ReadWord(ptr, 3));

        uint[] backData = new uint[4];
        _provider.CopyDeviceToHost(backData, ptr, 4);
        Assert.Equal(hostData, backData);

        IntPtr destPtr = _provider.AllocateMemory(4);
        _provider.CopyDeviceToDevice(destPtr, ptr, 4);
        Assert.Equal(4u, _provider.ReadWord(destPtr, 3));

        _provider.FreeMemory(ptr);
        _provider.FreeMemory(destPtr);
    }

    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0xFFFFFFFF)]
    public void BasicMath_AddSub_WorksCorrectly(uint rightVal)
    {
        using var a = new GpuBigUInt(_workspace, new uint[] { 100, 0, 0, 0 });
        using var b = new GpuBigUInt(_workspace, new uint[] { rightVal, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 4);

        _provider.Add(_workspace, a, b, res);
        Assert.Equal(100 + rightVal, res.ToHostArray()[0]);

        _provider.Add(_workspace, a, rightVal, res);
        Assert.Equal(100 + rightVal, res.ToHostArray()[0]);

        using var a2 = new GpuBigUInt(_workspace, new uint[] { 100 + rightVal, 0, 0, 0 });
        _provider.Sub(_workspace, a2, b, res);
        Assert.Equal(100u, res.ToHostArray()[0]);

        _provider.Sub(_workspace, a2, rightVal, res);
        Assert.Equal(100u, res.ToHostArray()[0]);
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void BitwiseOperations_WorkCorrectly()
    {
        using var a = new GpuBigUInt(_workspace, new uint[] { 0b1100, 0, 0, 0 });
        using var b = new GpuBigUInt(_workspace, new uint[] { 0b1010, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 4);

        _provider.BitwiseAnd(_workspace, a, b, res);
        Assert.Equal(0b1000u, res.ToHostArray()[0]);

        _provider.BitwiseOr(_workspace, a, b, res);
        Assert.Equal(0b1110u, res.ToHostArray()[0]);

        _provider.BitwiseXor(_workspace, a, b, res);
        Assert.Equal(0b0110u, res.ToHostArray()[0]);

        _provider.BitwiseNot(_workspace, a, res);
        Assert.Equal(~0b1100u, res.ToHostArray()[0]);

        _provider.ShiftLeft(_workspace, a, 2, res);
        Assert.Equal(0b110000u, res.ToHostArray()[0]);

        _provider.ShiftRight(_workspace, a, 1, res);
        Assert.Equal(0b0110u, res.ToHostArray()[0]);
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void MathOperations_WorkCorrectly()
    {
        using var a = new GpuBigUInt(_workspace, new uint[] { 15, 0, 0, 0 });
        using var b = new GpuBigUInt(_workspace, new uint[] { 4, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 4);

        _provider.Mul(_workspace, a, b, res);
        Assert.Equal(60u, res.ToHostArray()[0]);

        _provider.Mul(_workspace, a, 4u, res);
        Assert.Equal(60u, res.ToHostArray()[0]);

        _provider.Square(_workspace, a.DevicePointer, 2);
        Assert.Equal(225u, a.ToHostArray()[0]);

        using var a2 = new GpuBigUInt(_workspace, new uint[] { 12, 0, 0, 0 });
        using var b2 = new GpuBigUInt(_workspace, new uint[] { 18, 0, 0, 0 });
        _provider.Gcd(_workspace, a2, b2, res);
        Assert.Equal(6u, res.ToHostArray()[0]);
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void ModInverse_ThrowsException_WhenNotCoprime()
    {
        using var a = new GpuBigUInt(_workspace, new uint[] { 15, 0, 0, 0 });
        using var n = new GpuBigUInt(_workspace, new uint[] { 20, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 4);

        var exception = Assert.Throws<ArithmeticException>(() => _provider.ModInverse(_workspace, a, n, res));
        Assert.Contains("ModInverse failed", exception.Message);
    }
    
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void ModInverse_WorksCorrectly()
    {
        using var a = new GpuBigUInt(_workspace, new uint[] { 3, 0, 0, 0 });
        using var n = new GpuBigUInt(_workspace, new uint[] { 11, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 4);

        _provider.ModInverse(_workspace, a, n, res);
        Assert.Equal(4u, res.ToHostArray()[0]);
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void GraphOperations_DoNothing()
    {
        IntPtr stream = new IntPtr(123);
        _provider.BeginCapture(stream);
        var graph = _provider.EndCapture(stream);
        Assert.Equal(IntPtr.Zero, graph);

        var exec = _provider.InstantiateGraph(graph);
        Assert.Equal(IntPtr.Zero, exec);

        _provider.LaunchGraph(exec, stream);
        _provider.DestroyGraph(graph, exec);
    }
    
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Checks_WorkCorrectly()
    {
        using var zero = new GpuBigUInt(_workspace, new uint[] { 0, 0, 0, 0 });
        using var one = new GpuBigUInt(_workspace, new uint[] { 1, 0, 0, 0 });
        using var big = new GpuBigUInt(_workspace, new uint[] { 255, 0, 0, 0 });

        Assert.True(_provider.IsZero(_workspace, zero));
        Assert.False(_provider.IsZero(_workspace, one));

        Assert.True(_provider.IsOne(_workspace, one));
        Assert.False(_provider.IsOne(_workspace, zero));

        Assert.Equal(0, _provider.GetBitLength(_workspace, zero));
        Assert.Equal(1, _provider.GetBitLength(_workspace, one));
        Assert.Equal(8, _provider.GetBitLength(_workspace, big));

        Assert.Equal(0, _provider.CompareTo(_workspace, zero, zero));
        Assert.Equal(-1, _provider.CompareTo(_workspace, zero, one));
        Assert.Equal(1, _provider.CompareTo(_workspace, big, one));
    }
    
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void InverseSqrtMega_And_ReciprocalMega_WorkCorrectly()
    {
        using var a = new GpuBigUInt(_workspace, new uint[] { 4, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 8);

        _provider.InverseSqrtMega(_workspace, a, res, 0, 2, 0);
        Assert.False(_provider.IsZero(_workspace, res));
        
        _provider.ReciprocalMega(_workspace, a, res, 0, 32, 0, 0, 32);
        Assert.False(_provider.IsZero(_workspace, res));
    }
    
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void InverseSqrtMega_And_ReciprocalMega_ThrowOnZero()
    {
        using var zero = new GpuBigUInt(_workspace, new uint[] { 0, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 8);

        Assert.Throws<DivideByZeroException>(() => _provider.InverseSqrtMega(_workspace, zero, res, 0, 2, 0));
        Assert.Throws<DivideByZeroException>(() => _provider.ReciprocalMega(_workspace, zero, res, 0, 32, 0, 0, 32));
    }
}
