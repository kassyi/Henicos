using System;
using System.Text;
using System.Threading.Tasks;
using Kassyi.GSharp.Cryptography;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Fatbin;
using Kassyi.GSharp.Runtime.Interop;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public class GpuKernelLoaderAotTests
{
    // A minimal valid PTX string (acting as binary data) to satisfy cuModuleLoadDataEx
    private static readonly byte[] s_validPtx;

    static GpuKernelLoaderAotTests()
    {
        var basePtx = Encoding.UTF8.GetBytes(".version 7.0\n.target sm_50\n.address_size 64\n.visible .entry empty_kernel() { ret; }\0");
        var signature = Ed25519.Sign(basePtx, Ed25519.DefaultDevKeySeed);
        s_validPtx = [.. basePtx, .. signature];
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    public void Test_LoadAotKernel_ParallelContention()
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;

        // Execute in parallel on multiple threads to verify thread-safety and CUDA context management
        Parallel.For(0, 100, i =>
        {
            var hfunc = GpuKernelLoader.LoadAotKernel(s_validPtx, "empty_kernel");
            Assert.NotEqual(IntPtr.Zero, hfunc);
            
            // Call SynchronizeDevice to ensure context binding is correct per-thread
            GpuKernelLoader.SynchronizeDevice();
        });
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_LoadAotKernel_IncompatibleFatbin_ThrowsInvalidOperationException()
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;

        // Create a mock Fatbin targeted at sm_99 (a future architecture higher than any current physical GPU)
        byte[] mockFatbin = FatbinParserTests.CreateMockFatbin(FatbinItemInfo.TypePtx, 990, 99, 0);
        byte[] signature = Ed25519.Sign(mockFatbin, Ed25519.DefaultDevKeySeed);
        byte[] signedFatbin = [.. mockFatbin, .. signature];

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            GpuKernelLoader.LoadAotKernel(signedFatbin, "dummy_kernel");
        });

        Assert.Contains("does not contain any PTX or cuBIN compatible with the current GPU", ex.Message);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_LoadModule_IncompatibleFatbin_ThrowsInvalidOperationException()
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;

        // Create a mock Fatbin targeted at sm_99 (a future architecture)
        byte[] mockFatbin = FatbinParserTests.CreateMockFatbin(FatbinItemInfo.TypeCubin, 990, 99, 0);
        byte[] signature = Ed25519.Sign(mockFatbin, Ed25519.DefaultDevKeySeed);
        byte[] signedFatbin = [.. mockFatbin, .. signature];

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            GpuKernelLoader.LoadModule(signedFatbin);
        });

        Assert.Contains("does not contain any PTX or cuBIN compatible with the current GPU", ex.Message);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_LoadModule_And_LoadAotKernel_SharesCachedModule()
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;

        var module1 = GpuKernelLoader.LoadModule(s_validPtx);
        var module2 = GpuKernelLoader.LoadModule(s_validPtx);
        Assert.NotEqual(IntPtr.Zero, module1);
        Assert.Equal(module1, module2);

        var hfunc = GpuKernelLoader.LoadAotKernel(s_validPtx, "empty_kernel");
        Assert.NotEqual(IntPtr.Zero, hfunc);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_CreateModuleLoadException_NoBinaryForGpu_ListsContentsAndDevice()
    {
        // An sm_90a-style cubin passes the pre-check on 9.0 but the driver can still refuse it; the message must then
        // match the pre-check's message.
        byte[] fatbinBytes = FatbinParserTests.CreateMockFatbin(
            (FatbinItemInfo.TypeCubin, 86u, (ushort)8, (ushort)6),
            (FatbinItemInfo.TypePtx, 86u, (ushort)8, (ushort)6)
        );
        Assert.True(FatbinParser.TryParse(fatbinBytes, out var image));

        Exception ex = GpuKernelLoader.CreateModuleLoadException(CUresult.CudaErrorNoBinaryForGpu, image, 7, 5);

        var invalid = Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("does not contain any PTX or cuBIN compatible with the current GPU (sm_75)", invalid.Message);
        Assert.Contains("cuBIN sm_86", invalid.Message);
        Assert.Contains("PTX compute_86", invalid.Message);
        Assert.Contains("GSharpTargetArchitectures", invalid.Message);
        Assert.Contains("keep the default entries (for example compute_86,sm_86;compute_86,compute_86;compute_75,sm_75)",
            invalid.Message);
        var inner = Assert.IsType<CudaException>(invalid.InnerException);
        Assert.Equal(CUresult.CudaErrorNoBinaryForGpu, inner.Result);
        Assert.Equal(GpuKernelLoader.DescribeIncompatibleFatbin(image, 7, 5), invalid.Message);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CreateModuleLoadException_NoBinaryForGpu_UnparsedFatbin_SaysContentsUnknown()
    {
        Exception ex = GpuKernelLoader.CreateModuleLoadException(CUresult.CudaErrorNoBinaryForGpu, null, 12, 0);

        var invalid = Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("(sm_120)", invalid.Message);
        Assert.Contains("could not be parsed", invalid.Message);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CreateModuleLoadException_NoBinaryForGpu_EmptyFatbin_SaysNoImages()
    {
        var empty = new FatbinImage(0xBA55ED50u, 1, 16, false, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, []);

        Exception ex = GpuKernelLoader.CreateModuleLoadException(CUresult.CudaErrorNoBinaryForGpu, empty, 8, 6);

        var invalid = Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("(sm_86). It contains no images.", invalid.Message);
    }

    // The default fatbin carries compute_86 PTX; a driver older than the toolkit cannot JIT-compile it
    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_CreateModuleLoadException_UnsupportedPtxVersion_NamesOldDriverAndFixes()
    {
        byte[] fatbinBytes = FatbinParserTests.CreateMockFatbin(
            (FatbinItemInfo.TypeCubin, 86u, (ushort)8, (ushort)6),
            (FatbinItemInfo.TypePtx, 86u, (ushort)8, (ushort)6)
        );
        Assert.True(FatbinParser.TryParse(fatbinBytes, out var image));

        Exception ex = GpuKernelLoader.CreateModuleLoadException(CUresult.CudaErrorUnsupportedPtxVersion, image, 12, 0);

        var invalid = Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("(sm_120)", invalid.Message);
        Assert.Contains("driver is older than the CUDA Toolkit", invalid.Message);
        Assert.Contains("PTX compute_86", invalid.Message);
        Assert.Contains("Update the NVIDIA driver", invalid.Message);
        Assert.Contains("compute_86,sm_86;compute_86,compute_86;compute_120,sm_120", invalid.Message);
        var inner = Assert.IsType<CudaException>(invalid.InnerException);
        Assert.Equal(CUresult.CudaErrorUnsupportedPtxVersion, inner.Result);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_CreateModuleLoadException_OtherError_StaysCudaException()
    {
        Exception ex = GpuKernelLoader.CreateModuleLoadException(CUresult.CudaErrorInvalidImage, null, 8, 6);

        var cuda = Assert.IsType<CudaException>(ex);
        Assert.Equal(CUresult.CudaErrorInvalidImage, cuda.Result);
    }
}
