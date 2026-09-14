using Xunit;

namespace Kassyi.GSharp.Tests.Helpers
{
    public static class ExecutionCapabilityChecker
    {
        private static readonly bool s_isWdacBlocked = CheckWdacBlock();
        private static readonly bool s_isCudaDriverMissing = CheckCudaDriverMissing();

        private static bool CheckWdacBlock()
        {
            try
            {
#if !GSHARP_CUDA
                if (!Kassyi.GSharp.Runtime.Jit.NvccCompiler.IsMsvcAvailable) return false;
                var source = "extern \"C\" __declspec(dllexport) void WdacTest() {}";
                Kassyi.GSharp.Runtime.Jit.CpuJitEngine.GetOrCompileKernelPtr("WdacTest", source);
#endif
                return false;
            }
            catch (System.IO.FileLoadException ex) when ((uint)ex.HResult == 0x800711C7)
            {
                return true;
            }
            catch (System.Exception ex) when (ex.InnerException is System.IO.FileLoadException fle && (uint)fle.HResult == 0x800711C7)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckCudaDriverMissing()
        {
            try
            {
#if GSHARP_CUDA
                Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuInit(0);
#endif
                return false;
            }
            catch (System.DllNotFoundException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void EnsureExecutionCapability()
        {
#if GSHARP_CUDA
            Skip.IfNot(Kassyi.GSharp.Runtime.Jit.NvccCompiler.IsNvccAvailable, "CUDA Toolkit (NVCC/NVRTC) is not available on this machine.");
            Skip.If(s_isCudaDriverMissing, "NVIDIA GPU driver (nvcuda.dll) is not available on this machine.");
#else
            Skip.IfNot(Kassyi.GSharp.Runtime.Jit.NvccCompiler.IsMsvcAvailable, "MSVC is not available on this machine.");
            Skip.If(s_isWdacBlocked, "Execution is blocked by Windows Defender Application Control (WDAC).");
#endif
        }

        public static void EnsureGpuExecutionCapability()
        {
#if GSHARP_CUDA
            Skip.IfNot(Kassyi.GSharp.Runtime.Jit.NvccCompiler.IsNvccAvailable, "CUDA Toolkit (NVCC/NVRTC) is not available on this machine.");
            Skip.If(s_isCudaDriverMissing, "NVIDIA GPU driver (nvcuda.dll) is not available on this machine.");
#else
            Skip.IfNot(false, "This test requires a GPU execution engine (CUDA) and cannot run in CPU mode.");
#endif
        }
    }
}
