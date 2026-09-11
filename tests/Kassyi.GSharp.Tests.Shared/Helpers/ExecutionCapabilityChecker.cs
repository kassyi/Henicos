using Xunit;

namespace Kassyi.GSharp.Tests.Helpers
{
    public static class ExecutionCapabilityChecker
    {
        public static void EnsureGpuExecutionCapability()
        {
#if GSHARP_CUDA
            Skip.IfNot(Kassyi.GSharp.Runtime.Jit.JitEngine.IsDriverAvailable, "NVIDIA GPU driver (nvcuda.dll) is not available on this machine.");
#else
            Skip.IfNot(false, "This test requires a GPU execution engine (CUDA) and cannot run in CPU mode.");
#endif
        }
    }
}
