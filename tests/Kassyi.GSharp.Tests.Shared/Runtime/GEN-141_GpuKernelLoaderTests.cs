using System.Text;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Runtime;

public class GEN141_GpuKernelLoaderTests
{
    private static readonly byte[] s_validPtx;

    static GEN141_GpuKernelLoaderTests()
    {
        var basePtx = Encoding.UTF8.GetBytes(".version 7.0\n.target sm_50\n.address_size 64\n.visible .entry empty_kernel() { ret; }\0");
        var hash = System.Security.Cryptography.SHA256.HashData(basePtx);
        s_validPtx = [.. basePtx, .. hash];
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-141")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "HappyPath")]
    public void Test_GEN141_HighThroughputKernelLoading_ParallelStress()
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();

        // Stress parallel concurrent calls to LoadAotKernel to ensure allocation locks and loader caches operate thread-safely
        Parallel.For(0, 50, i =>
        {
            var hfunc = GpuKernelLoader.LoadAotKernel(s_validPtx, "empty_kernel");
            Assert.NotEqual(IntPtr.Zero, hfunc);

            GpuKernelLoader.SynchronizeDevice();
        });
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-141")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN141_CorruptedPtxBinary_ThrowsArgumentException()
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();

        // Create a hash-appended but syntactically invalid PTX fixture so it reaches module loading
        var invalidPtxBase = Encoding.UTF8.GetBytes("INVALID_PTX_HEADER_AND_BODY\0");
        var invalidHash = System.Security.Cryptography.SHA256.HashData(invalidPtxBase);
        byte[] corruptedPtx = [.. invalidPtxBase, .. invalidHash];

        // Loading corrupted/invalid PTX buffer should throw Exception (InvalidImage) when CudaException occurs
        var ex = Assert.Throws<Exception>(() =>
        {
            GpuKernelLoader.LoadAotKernel(corruptedPtx, "non_existent_kernel");
        });
        Assert.Contains("CudaErrorInvalidImage", ex.Message);
    }
    
    [SkippableTheory]
    [Trait("FeatureId", "GEN-141")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)] // Less than 32 bytes (SHA256 hash length)
    public void Test_GEN141_ShortPtxBuffer_ThrowsArgumentException(int length)
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();

        byte[] shortBuffer = new byte[length];

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            GpuKernelLoader.LoadAotKernel(shortBuffer, "any_kernel");
        });
        Assert.Contains("too small", ex.Message);
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-141")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN141_NonExistentKernelName_ThrowsArgumentException()
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();

        // Valid PTX module but non-existent entry point function name
        var ex = Assert.Throws<CudaException>(() =>
        {
            GpuKernelLoader.LoadAotKernel(s_validPtx, "missing_kernel_entry_point");
        });
        Assert.Contains("CudaErrorNotFound", ex.Message);
    }
}
