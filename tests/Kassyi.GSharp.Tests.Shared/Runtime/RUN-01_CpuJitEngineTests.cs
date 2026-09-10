using System.Collections.Generic;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Jit;
using Xunit;

namespace Kassyi.GSharp.Tests.Runtime;

public class RUN_01_CpuJitEngineTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int AddFunc(int a, int b);

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("FeatureId", "RUN-01")]
    public void Test_RUN01_CpuJitEngine_CompilesAndExecutes_Delegate()
    {
        var source = @"
extern ""C"" __declspec(dllexport) int Add(int a, int b) {
    return a + b;
}
";
        var addFunc = CpuJitEngine.GetOrCompileKernel<AddFunc>("Add", source);
        var result = addFunc(2, 3);
        Assert.Equal(5, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("FeatureId", "RUN-01")]
    public void Test_RUN01_CpuJitEngine_CompilesWithMacros()
    {
        var source = @"
extern ""C"" __declspec(dllexport) int Add(int a, int b) {
    return a + b + MY_MACRO_OFFSET;
}
";
        var macros = new[] { new KeyValuePair<string, string>("MY_MACRO_OFFSET", "10") };
        var addFunc = CpuJitEngine.GetOrCompileKernel<AddFunc>("Add", source, macros: macros);
        var result = addFunc(2, 3);
        Assert.Equal(15, result);
    }
}
