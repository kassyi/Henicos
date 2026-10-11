using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public class BclIntrinsicMapAttributeTests
{
    [Fact]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        var parameters = new[] { "System.Int32" };
        var attr = new BclIntrinsicMapAttribute(
            "System.Math",
            "Abs",
            parameters,
            "__nv_abs");

        attr.CastFirstArgumentTo = "int";

        Assert.Equal("System.Math", attr.BclTypeFullName);
        Assert.Equal("Abs", attr.MethodName);
        Assert.Single(attr.ParameterTypeFullNames);
        Assert.Equal("System.Int32", attr.ParameterTypeFullNames[0]);
        Assert.Equal("__nv_abs", attr.CudaIntrinsicName);
        Assert.Equal("int", attr.CastFirstArgumentTo);
    }
}
