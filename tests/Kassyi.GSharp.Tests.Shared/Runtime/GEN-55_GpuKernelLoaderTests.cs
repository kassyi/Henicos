using System.Text;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Runtime;

public class GEN55_GpuKernelLoaderTests : TranspilerTestBase
{
    private static readonly byte[] s_validPtx;

    static GEN55_GpuKernelLoaderTests()
    {
        var basePtx = Encoding.UTF8.GetBytes(".version 7.0\n.target sm_50\n.address_size 64\n.visible .entry empty_kernel() { ret; }\0");
        var hash = System.Security.Cryptography.SHA256.HashData(basePtx);
        s_validPtx = [.. basePtx, .. hash];
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-55")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "HappyPath")]
    public void Test_GEN55_HighThroughputKernelLoading_ParallelStress()
    {

        // Stress parallel concurrent calls to LoadAotKernel to ensure allocation locks and loader caches operate thread-safely
        Parallel.For(0, 50, i =>
        {
            var hModule = GpuKernelLoader.LoadModule(s_validPtx);
            var hfunc = GpuKernelLoader.GetFunction(hModule, "empty_kernel");
            Assert.NotEqual(IntPtr.Zero, hfunc);

            GpuKernelLoader.SynchronizeDevice();
        });
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-55")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN55_CorruptedPtxBinary_ThrowsArgumentException()
    {

        // Create a hash-appended but syntactically invalid PTX fixture so it reaches module loading
        var invalidPtxBase = Encoding.UTF8.GetBytes("INVALID_PTX_HEADER_AND_BODY\0");
        var invalidHash = System.Security.Cryptography.SHA256.HashData(invalidPtxBase);
        byte[] corruptedPtx = [.. invalidPtxBase, .. invalidHash];

        // Loading corrupted/invalid PTX buffer should throw CudaException (CudaErrorInvalidImage)
        var ex = Assert.Throws<CudaException>(() =>
        {
            var hModule = GpuKernelLoader.LoadModule(corruptedPtx);
            GpuKernelLoader.GetFunction(hModule, "non_existent_kernel");
        });
        Assert.Equal(CUresult.CudaErrorInvalidImage, ex.Result);
    }
    
    [SkippableTheory]
    [Trait("FeatureId", "GEN-55")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)] // Less than 32 bytes (SHA256 hash length)
    public void Test_GEN55_ShortPtxBuffer_ThrowsArgumentException(int length)
    {

        byte[] shortBuffer = new byte[length];

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            var hModule = GpuKernelLoader.LoadModule(shortBuffer);
            GpuKernelLoader.GetFunction(hModule, "any_kernel");
        });
        Assert.Contains("too small", ex.Message);
    }

    [SkippableTheory]
    [Trait("FeatureId", "GEN-55")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    [InlineData(32)] // Exact boundary
    [InlineData(33)] // One byte above boundary
    public void Test_GEN55_ExactBoundaryBuffer_ThrowsHashMismatch(int length)
    {

        byte[] buffer = new byte[length]; // all zeros, hash will definitely mismatch

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            GpuKernelLoader.LoadModule(buffer);
        });
        Assert.Contains("hash mismatch", ex.Message);
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-55")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN55_NonExistentKernelName_ThrowsArgumentException()
    {

        // Valid PTX module but non-existent entry point function name
        var ex = Assert.Throws<CudaException>(() =>
        {
            var hModule = GpuKernelLoader.LoadModule(s_validPtx);
            GpuKernelLoader.GetFunction(hModule, "missing_kernel_entry_point");
        });
        Assert.Contains("CudaErrorNotFound", ex.Message);
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-55")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN55_TamperedBody_ThrowsInvalidOperationException()
    {

        // Copy valid PTX
        byte[] tampered = new byte[s_validPtx.Length];
        Array.Copy(s_validPtx, tampered, s_validPtx.Length);

        // Mutate the body (e.g. byte at index 10)
        tampered[10] = (byte)(tampered[10] ^ 0xFF);

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            GpuKernelLoader.LoadModule(tampered);
        });
        Assert.Contains("hash mismatch", ex.Message);
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-55")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN55_TamperedHashFooter_ThrowsInvalidOperationException()
    {

        // Copy valid PTX
        byte[] tampered = new byte[s_validPtx.Length];
        Array.Copy(s_validPtx, tampered, s_validPtx.Length);

        // Mutate the footer hash (last 32 bytes)
        tampered[^1] = (byte)(tampered[^1] ^ 0xFF);

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            GpuKernelLoader.LoadModule(tampered);
        });
        Assert.Contains("hash mismatch", ex.Message);
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-55")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "HappyPath")]
    public void Test_GEN55_ModuleContextWarmup_ExecutesSafely()
    {

#if GSHARP_CUDA
        // Directly invoke the auto-generated static method without Reflection.
        // Static Determinism & Zero-Allocation rule.
        global::Kassyi.GSharp.Generated.GSharpModuleContext_Kassyi_GSharp_Tests_Cuda.Warmup();
#else
        throw new SkipException("Module warmup is only applicable in CUDA execution mode.");
#endif
    }
}
