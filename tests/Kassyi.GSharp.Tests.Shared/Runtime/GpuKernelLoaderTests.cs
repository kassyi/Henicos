using System;
using System.Text;
using System.Threading.Tasks;
using Kassyi.GSharp.Cryptography;
using Kassyi.GSharp.Runtime.Aot;
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
    [Trait("Category", "Runtime")]
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
}
