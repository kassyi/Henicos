using System;
using System.Text;
using System.Threading.Tasks;
using Kassyi.GSharp.Cryptography;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Fatbin;
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
}
