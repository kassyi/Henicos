#nullable enable
using System;
using System.Threading.Tasks;
using Xunit;
using Kassyi.GSharp.Runtime.Jit;
using System.Security.Cryptography;
using System.Text;

namespace Kassyi.GSharp.Tests.Runtime;

public class JitEngineAotTests
{
    // A minimal valid PTX string (acting as binary data) to satisfy cuModuleLoadDataEx
    private static readonly byte[] s_validPtx;

    static JitEngineAotTests()
    {
        var basePtx = Encoding.UTF8.GetBytes(".version 7.0\n.target sm_50\n.address_size 64\n.visible .entry empty_kernel() { ret; }\0");
        var hash = System.Security.Cryptography.SHA256.HashData(basePtx);
        s_validPtx = [.. basePtx, .. hash];
    }

    [Fact]
    [Trait("FeatureId", "RT-JIT")]
    [Trait("Category", "Runtime")]
    public void Test_LoadAotKernel_ParallelContention()
    {
        if (!JitEngine.IsDriverAvailable) return;

        // Execute in parallel on multiple threads to verify thread-safety and CUDA context management
        Parallel.For(0, 100, i =>
        {
            var hfunc = JitEngine.LoadAotKernel(s_validPtx, "empty_kernel");
            Assert.NotEqual(IntPtr.Zero, hfunc);
            
            // Call SynchronizeDevice to ensure context binding is correct per-thread
            JitEngine.SynchronizeDevice();
        });
    }
}
