using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class WmmaTests
{
    partial void AssertWmmaDialectSource(string source)
    {
        // The CPU dialect does not implement the operations (GSH0054): each call is a runtime failure
        const string NotImplemented = " is not implemented by the StandardCppDialect dialect\")";
        Assert.Contains("__gsharp_unsupported(\"Wmma.Load" + NotImplemented, source);
        Assert.Contains("__gsharp_unsupported(\"Wmma.Store" + NotImplemented, source);
        Assert.DoesNotContain("__gsharp_wmma_load", source);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void WmmaInt8RowMajor_CpuDialectLaunch_ThrowsNotSupportedException()
    {
        var a = new sbyte[256];
        var b = new sbyte[256];
        var c = new int[256];
        var d = new int[256];

        Assert.Throws<NotSupportedException>(() =>
            WmmaKernels.LaunchWmmaInt8RowMajorKernel(a, b, c, d, blockDimX: 32));
    }
}
