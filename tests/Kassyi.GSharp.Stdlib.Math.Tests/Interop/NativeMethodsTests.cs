using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Xunit;
using Kassyi.GSharp.Stdlib.Math.Interop;

namespace Kassyi.GSharp.Stdlib.Math.Tests.Interop;

public class NativeMethodsTests
{
    [Fact]
    public void NativeMethods_TypeInit_ShouldNotThrowIfCudaFound()
    {
        // Simply access the type to ensure static constructor runs without unhandled exceptions
        var type = typeof(NativeMethods);
        Assert.NotNull(type);
    }
}
