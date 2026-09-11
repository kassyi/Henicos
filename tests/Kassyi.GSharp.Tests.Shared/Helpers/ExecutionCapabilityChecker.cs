using Xunit;

namespace Kassyi.GSharp.Tests.Helpers
{
    public static class ExecutionCapabilityChecker
    {
        public static void EnsureExecutionCapability()
        {
#if GSHARP_CUDA
            Skip.IfNot(Kassyi.GSharp.Runtime.Jit.NvccCompiler.IsNvccAvailable, "CUDA Toolkit (NVCC/NVRTC) is not available on this machine.");
#else
            Skip.IfNot(Kassyi.GSharp.Runtime.Jit.NvccCompiler.IsMsvcAvailable, "MSVC is not available on this machine.");
#endif
        }

        public static void EnsureGpuExecutionCapability()
        {
#if GSHARP_CUDA
            Skip.IfNot(Kassyi.GSharp.Runtime.Jit.NvccCompiler.IsNvccAvailable, "CUDA Toolkit (NVCC/NVRTC) is not available on this machine.");
#else
            Skip.IfNot(false, "This test requires a GPU execution engine (CUDA) and cannot run in CPU mode.");
#endif
        }
    }
}
