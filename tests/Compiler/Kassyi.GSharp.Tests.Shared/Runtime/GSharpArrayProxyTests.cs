#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// The ways to build the array argument of a <c>Launch{Kernel}Ptr</c> launcher: each carries the length the kernel sees,
/// and a raw pointer, which has none, does not convert (#720).
/// </summary>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class GSharpArrayProxyTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Constructor_KeepsTheAddressAndTheLength()
    {
        var proxy = new GSharpArrayProxy(new IntPtr(0x1000), 8);

        Assert.Equal(new IntPtr(0x1000), proxy.Data);
        Assert.Equal(8, proxy.Length);
        Assert.Equal(0, proxy.Flags);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Constructor_ANullArrayOfNoElementsIsTheDefault()
    {
        Assert.Equal(default, new GSharpArrayProxy(IntPtr.Zero, 0));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Constructor_RejectsANegativeLength()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new GSharpArrayProxy(new IntPtr(0x1000), -1));
        Assert.Equal("length", error.ParamName);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Constructor_RejectsANullArrayWithElements()
    {
        var error = Assert.Throws<ArgumentException>(() => new GSharpArrayProxy(IntPtr.Zero, 3));
        Assert.Equal("length", error.ParamName);
    }

    // An empty buffer allocates nothing; as a kernel argument it is an empty array, not a null one
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GpuBufferToProxy_AnEmptyBufferIsAnEmptyArray()
    {
        using var buffer = new GpuBuffer<int>(0);

        var proxy = buffer.ToProxy();

        Assert.Equal(GSharpArrayProxy.EmptyArrayData, proxy.Data);
        Assert.Equal(0, proxy.Length);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GpuBufferToProxy_RejectsADisposedBuffer()
    {
        var buffer = new GpuBuffer<int>(0);
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffer.ToProxy());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GpuBufferConversion_NullIsANullArray()
    {
        GpuBuffer<int>? buffer = null;

        GSharpArrayProxy proxy = buffer;

        Assert.Equal(default, proxy);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void IntPtr_DoesNotConvertToAProxy()
    {
        var tree = CSharpSyntaxTree.ParseText(RoslynTestUtility.ReadTestCase("Runtime/ArrayProxyConversionCases.cs"));
        var runtime = MetadataReference.CreateFromFile(typeof(GSharpArrayProxy).Assembly.Location);
        var compilation = CSharpCompilation.Create(
            "ArrayProxyConversionCases",
            [tree],
            RoslynTestUtility.BaseReferences.Append(runtime),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var errors = compilation.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Equal(2, errors.Count);
        Assert.All(errors, static e => Assert.Equal("CS0029", e.Id));
    }
}
