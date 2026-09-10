#if GSHARP_CUDA
using System;
using Kassyi.GSharp.Runtime.Jit;
using Xunit;

namespace Kassyi.GSharp.Tests.Runtime;

[Trait("Category", "GpuExecution")]
public class GEN_42_JitEngineCacheTests
{
    private const string SimpleKernelSource = """
        extern "C" __global__ void TestCacheKernel(int* data)
        {
            data[0] = 42;
        }
        """;

    private const string AnotherKernelSource = """
        extern "C" __global__ void TestAnotherKernel(int* data)
        {
            data[0] = 84;
        }
        """;

    [Fact]
    [Trait("FeatureId", "GEN-42")]
    public void Test_GEN42_JitEngine_ReturnsCachedFunctionPointerOnRepeatedCalls()
    {
        // First call: compiles and caches the function
        var initialMissCount = JitEngine.CacheMissCount;
        var funcPtr1 = JitEngine.GetOrCompileKernel("TestCacheKernel", SimpleKernelSource);
        Assert.NotEqual(IntPtr.Zero, funcPtr1);

        // Second call with identical kernel and source: must hit in-memory cache
        var missCountAfterFirst = JitEngine.CacheMissCount;
        var funcPtr2 = JitEngine.GetOrCompileKernel("TestCacheKernel", SimpleKernelSource);
        var missCountAfterSecond = JitEngine.CacheMissCount;

        Assert.Equal(funcPtr1, funcPtr2);
        Assert.Equal(missCountAfterFirst, missCountAfterSecond);
    }

    [Fact]
    [Trait("FeatureId", "GEN-42")]
    public void Test_GEN42_JitEngine_DistinguishesDifferentKernelSources()
    {
        var funcPtr1 = JitEngine.GetOrCompileKernel("TestCacheKernel", SimpleKernelSource);
        var funcPtr2 = JitEngine.GetOrCompileKernel("TestAnotherKernel", AnotherKernelSource);

        Assert.NotEqual(IntPtr.Zero, funcPtr1);
        Assert.NotEqual(IntPtr.Zero, funcPtr2);
        Assert.NotEqual(funcPtr1, funcPtr2);
    }
}
#endif
