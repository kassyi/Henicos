#nullable enable
using System;
using System.Threading.Tasks;
using Xunit;
using Kassyi.GSharp.Runtime.Aot;
using System.Security.Cryptography;
using System.Text;

namespace Kassyi.GSharp.Tests.Runtime;

public class GpuKernelLoaderAotTests
{
    // A minimal valid PTX string (acting as binary data) to satisfy cuModuleLoadDataEx
    private static readonly byte[] s_validPtx;

    static GpuKernelLoaderAotTests()
    {
        var basePtx = Encoding.UTF8.GetBytes(".version 7.0\n.target sm_50\n.address_size 64\n.visible .entry empty_kernel() { ret; }\0");
        var hash = System.Security.Cryptography.SHA256.HashData(basePtx);
        s_validPtx = [.. basePtx, .. hash];
    }

    [Fact]
    [Trait("FeatureId", "GEN-43")]
    [Trait("Category", "Runtime")]
    public void Test_GEN43_LoadAotKernel_ParallelContention()
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;

        // Execute in parallel on multiple threads to verify thread-safety and CUDA context management
        Parallel.For(0, 100, i =>
        {
            var hModule = GpuKernelLoader.LoadModule(s_validPtx);
            var hfunc = GpuKernelLoader.GetFunction(hModule, "empty_kernel");
            Assert.NotEqual(IntPtr.Zero, hfunc);
            
            // Call SynchronizeDevice to ensure context binding is correct per-thread
            GpuKernelLoader.SynchronizeDevice();
        });
    }
}
