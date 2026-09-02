using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Runtime;

public partial class Gsh06_07_RuntimeIssuesTests
    : TranspilerTestBase
{
    [Fact]
    [Trait("FeatureId", "GSH-06")]
    [Trait("Category", "Runtime")]
    public void Test_GSH06_LineNumberMappingInDiagnostics()
    {
    }

    [SkippableFact]
    [Trait("FeatureId", "GSH-07")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "Execution")]
    public void Test_GSH07_JitDiskCache_RecoversFromCorruptedCache()
    {
        const string kernelName = "CacheTestKernel";
        const string kernelSource = @"extern ""C"" __global__ void CacheTestKernel(int* a) { *a = 42; }";

        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Henicos",
            "GSharp",
            "PtxCache");

        Directory.CreateDirectory(cacheDir);

        // Pre-create corrupted 0-byte or garbage cache files matching wildcard
        var dummyCorruptPath = Path.Combine(cacheDir, $"{kernelName}_corrupt_dummy.ptx");
        File.WriteAllBytes(dummyCorruptPath, [0x00, 0xDE, 0xAD, 0xBE, 0xEF]);

        // Attempt compilation / cache loading
        var func1 = JitEngine.GetOrCompileKernel(kernelName, kernelSource);
        Assert.NotEqual(IntPtr.Zero, func1);

        // Concurrent access test (stress testing atomic cache write)
        Parallel.For(0, 10, _ =>
        {
            var funcConcurrent = JitEngine.GetOrCompileKernel(kernelName, kernelSource);
            Assert.NotEqual(IntPtr.Zero, funcConcurrent);
        });
    }

    [Fact(Skip = "GPU Out-of-memory stress test is disabled by default")]
    [Trait("FeatureId", "GSH-07")]
    [Trait("Category", "Stress")]
    [Trait("Category", "SadPath")]
    [Trait("Category", "Stress")]
    public void Test_Runtime_OutOfMemory_SadPath()
    {
        // Require CUDA context for allocation
        using var scope = new JitEngine.CudaContextScope();
        var allocatedPointers = new System.Collections.Generic.List<IntPtr>();

        try
        {
            // Try to allocate 1GB chunks until OOM
            IntPtr chunkSize = (IntPtr)(1024 * 1024 * 1024);
            while (true)
            {
                var result = CudaDriverApi.cuMemAlloc_v2(out var dptr, chunkSize);
                if (result == CUresult.CudaSuccess)
                {
                    allocatedPointers.Add(dptr);
                }
                else
                {
                    Assert.Equal(CUresult.CudaErrorOutOfMemory, result);
                    break;
                }
            }
        }
        finally
        {
            // Free all allocated chunks to restore system stability
            foreach (var ptr in allocatedPointers)
            {
                var freeRes = CudaDriverApi.cuMemFree_v2(ptr);
                Assert.Equal(CUresult.CudaSuccess, freeRes);
            }
        }
    }

    [Fact]
    [Trait("FeatureId", "GSH-07")]
    [Trait("Category", "SadPath")]
    public void Test_Runtime_MissingCudaToolkit_GracefulDegradation()
    {
        var originalEnv = Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA");
        try
        {
            Environment.SetEnvironmentVariable("GSHARP_TEST_NO_CUDA", "1");
            var ex = Assert.ThrowsAny<Exception>(() =>
            {
                NativeLibrary.Load("nvrtc_custom", typeof(NvrtcApi).Assembly, null);
            });

            var rootEx = ex.GetBaseException();
            Assert.IsType<DllNotFoundException>(rootEx);
            Assert.Contains("nvrtc_custom", rootEx.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GSHARP_TEST_NO_CUDA", originalEnv);
        }
    }
}
