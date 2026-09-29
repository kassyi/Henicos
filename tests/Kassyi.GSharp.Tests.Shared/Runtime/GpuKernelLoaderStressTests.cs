using System.Text;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Runtime;

[Trait("Category", "GpuExecution")]
public class GpuKernelLoaderStressTests
{
    private static readonly byte[] s_validPtx;

    static GpuKernelLoaderStressTests()
    {
        var basePtx = Encoding.UTF8.GetBytes(".version 7.0\n.target sm_50\n.address_size 64\n.visible .entry empty_kernel() { ret; }\0");
        var hash = System.Security.Cryptography.SHA256.HashData(basePtx);
        s_validPtx = [.. basePtx, .. hash];
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    [Trait("Category", "HappyPath")]
    public void Test_HighThroughputKernelLoading_ParallelStress()
    {

        // Stress parallel concurrent calls to LoadAotKernel to ensure allocation locks and loader caches operate thread-safely
        Parallel.For(0, 50, i =>
        {
            var hfunc = GpuKernelLoader.LoadAotKernel(s_validPtx, "empty_kernel");
            Assert.NotEqual(IntPtr.Zero, hfunc);

            GpuKernelLoader.SynchronizeDevice();
        });
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_CorruptedPtxBinary_ThrowsArgumentException()
    {

        // Create a hash-appended but syntactically invalid PTX fixture so it reaches module loading
        var invalidPtxBase = Encoding.UTF8.GetBytes("INVALID_PTX_HEADER_AND_BODY\0");
        var invalidHash = System.Security.Cryptography.SHA256.HashData(invalidPtxBase);
        byte[] corruptedPtx = [.. invalidPtxBase, .. invalidHash];

        // Loading corrupted/invalid PTX buffer should throw CudaException (CudaErrorInvalidImage)
        var ex = Assert.Throws<CudaException>(() =>
        {
            GpuKernelLoader.LoadAotKernel(corruptedPtx, "non_existent_kernel");
        });
        Assert.Equal(CUresult.CudaErrorInvalidImage, ex.Result);
    }

    [SkippableTheory]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)] // Less than 32 bytes (SHA256 hash length)
    public void Test_ShortPtxBuffer_ThrowsArgumentException(int length)
    {

        byte[] shortBuffer = new byte[length];

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            GpuKernelLoader.LoadAotKernel(shortBuffer, "any_kernel");
        });
        Assert.Contains("too small", ex.Message);
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_NonExistentKernelName_ThrowsArgumentException()
    {

        // Valid PTX module but non-existent entry point function name
        var ex = Assert.Throws<CudaException>(() =>
        {
            GpuKernelLoader.LoadAotKernel(s_validPtx, "missing_kernel_entry_point");
        });
        Assert.Contains("CudaErrorNotFound", ex.Message);
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_HashMismatch_ThrowsInvalidOperationException()
    {
        var basePtx = Encoding.UTF8.GetBytes(".version 7.0\n.target sm_50\n.address_size 64\n.visible .entry empty_kernel() { ret; }\0");
        var badHash = new byte[32]; // All zeros (incorrect hash)
        byte[] corruptedArtifact = [.. basePtx, .. badHash];

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            GpuKernelLoader.LoadAotKernel(corruptedArtifact, "empty_kernel");
        });
        Assert.Contains("hash mismatch", ex.Message);
    }
}
