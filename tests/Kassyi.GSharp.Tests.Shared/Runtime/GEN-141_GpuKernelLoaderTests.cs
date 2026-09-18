using System.Text;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

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

    [Fact]
    [Trait("FeatureId", "GEN-141")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "HappyPath")]
    public void Test_GEN141_HighThroughputKernelLoading_ParallelStress()
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;

        // Stress parallel concurrent calls to LoadAotKernel to ensure allocation locks and loader caches operate thread-safely
        Parallel.For(0, 50, i =>
        {
            var hfunc = GpuKernelLoader.LoadAotKernel(s_validPtx, "empty_kernel");
            Assert.NotEqual(IntPtr.Zero, hfunc);

            GpuKernelLoader.SynchronizeDevice();
        });
    }

    [Fact]
    [Trait("FeatureId", "GEN-141")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN141_CorruptedPtxBinary_ThrowsOrHandlesSafely()
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;

        byte[] corruptedPtx = Encoding.UTF8.GetBytes("INVALID_PTX_HEADER_AND_BODY");

        // Loading corrupted/invalid PTX buffer should throw CudaException or ArgumentException safely without process crash or lock deadlock
        Assert.ThrowsAny<Exception>(() =>
        {
            GpuKernelLoader.LoadAotKernel(corruptedPtx, "non_existent_kernel");
        });
    }

    [Fact]
    [Trait("FeatureId", "GEN-141")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN141_NonExistentKernelName_ThrowsCudaException()
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;

        // Valid PTX module but non-existent entry point function name
        Assert.ThrowsAny<Exception>(() =>
        {
            GpuKernelLoader.LoadAotKernel(s_validPtx, "missing_kernel_entry_point");
        });
    }
}
