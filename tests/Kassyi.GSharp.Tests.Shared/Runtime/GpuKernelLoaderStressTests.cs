using System.Text;
using Kassyi.GSharp.Cryptography;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuKernelLoaderStressTests
{
    private static readonly byte[] s_validPtx;

    static GpuKernelLoaderStressTests()
    {
        var basePtx = Encoding.UTF8.GetBytes(".version 7.0\n.target sm_50\n.address_size 64\n.visible .entry empty_kernel() { ret; }\0");
        var signature = Ed25519.Sign(basePtx, Ed25519.DefaultDevKeySeed);
        s_validPtx = [.. basePtx, .. signature];
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_HighThroughputKernelLoading_ParallelStress()
    {
        CudaCapability.EnsureGpuAvailable();

        // Stress parallel concurrent calls to LoadAotKernel to ensure allocation locks and loader caches operate thread-safely
        Parallel.For(0, 50, i =>
        {
            var hfunc = GpuKernelLoader.LoadAotKernel(s_validPtx, "empty_kernel");
            Assert.NotEqual(IntPtr.Zero, hfunc);

            GpuKernelLoader.SynchronizeDevice();
        });
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_CorruptedPtxBinary_ThrowsArgumentException()
    {
        CudaCapability.EnsureGpuAvailable();

        // Create a signature-appended but syntactically invalid PTX fixture so it reaches module loading
        var invalidPtxBase = Encoding.UTF8.GetBytes("INVALID_PTX_HEADER_AND_BODY\0");
        var validSignatureForInvalidPtx = Ed25519.Sign(invalidPtxBase, Ed25519.DefaultDevKeySeed);
        byte[] corruptedPtx = [.. invalidPtxBase, .. validSignatureForInvalidPtx];

        // Loading corrupted/invalid PTX buffer should throw CudaException (CudaErrorInvalidImage)
        var ex = Assert.Throws<CudaException>(() =>
        {
            GpuKernelLoader.LoadAotKernel(corruptedPtx, "non_existent_kernel");
        });
        Assert.Equal(CUresult.CudaErrorInvalidImage, ex.Result);
    }

    [SkippableTheory]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)] // Less than 64 bytes (Ed25519 signature length)
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
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_NonExistentKernelName_ThrowsArgumentException()
    {
        CudaCapability.EnsureGpuAvailable();

        // Valid PTX module but non-existent entry point function name
        var ex = Assert.Throws<CudaException>(() =>
        {
            GpuKernelLoader.LoadAotKernel(s_validPtx, "missing_kernel_entry_point");
        });
        Assert.Contains("CudaErrorNotFound", ex.Message);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_HashMismatch_ThrowsInvalidOperationException()
    {
        var basePtx = Encoding.UTF8.GetBytes(".version 7.0\n.target sm_50\n.address_size 64\n.visible .entry empty_kernel() { ret; }\0");
        var badSignature = new byte[64]; // All zeros (incorrect signature)
        byte[] corruptedArtifact = [.. basePtx, .. badSignature];

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            GpuKernelLoader.LoadAotKernel(corruptedArtifact, "empty_kernel");
        });
        Assert.Contains("signature", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

