using Xunit;

namespace Kassyi.GSharp.Tests.Helpers
{
    public static class ExecutionCapabilityChecker
    {
        public static void EnsureGpuExecutionCapability()
        {
#if GSHARP_CUDA
            if (System.Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA") == "1")
            {
                Skip.IfNot(false, "CUDA testing is disabled via GSHARP_TEST_NO_CUDA environment variable.");
            }
            Skip.IfNot(Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.IsDriverAvailable, "NVIDIA GPU driver (nvcuda.dll) is not available on this machine.");
#else
            Skip.IfNot(false, "This test requires a GPU execution engine (CUDA) and cannot run in CPU mode.");
#endif
        }
    }
}
