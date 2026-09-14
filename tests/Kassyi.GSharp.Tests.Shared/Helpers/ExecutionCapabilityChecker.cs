using Xunit;

namespace Kassyi.GSharp.Tests.Helpers
{
    /// <summary>
    /// Evaluates runtime execution capabilities (MSVC, NVCC, CUDA GPU availability, and WDAC policies)
    /// and safely skips tests that cannot execute in the current environment.
    /// <para>
    /// Note: Dynamic compilation blocks caused by Windows Defender Application Control (WDAC, 0x800711C7)
    /// will be fundamentally superseded once the AOT execution model (PR #133) lands.
    /// </para>
    /// </summary>
    public static class ExecutionCapabilityChecker
    {
        private static readonly System.Lazy<bool> s_isWdacBlocked = new System.Lazy<bool>(CheckWdacBlockSafe);
        private static readonly System.Lazy<bool> s_isCudaUnavailable = new System.Lazy<bool>(CheckCudaUnavailableSafe);

        // Separated with NoInlining so that JIT assembly resolution of Kassyi.GSharp.Runtime
        // occurs inside this method and can be trapped by the caller's try-catch.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool CheckWdacBlock()
        {
#if !GSHARP_CUDA
            if (!Kassyi.GSharp.Runtime.Jit.NvccCompiler.IsMsvcAvailable) return false;
            var source = "extern \"C\" __declspec(dllexport) void WdacTest() {}";
            Kassyi.GSharp.Runtime.Jit.CpuJitEngine.GetOrCompileKernelPtr("WdacTest", source);
#endif
            return false;
        }

        private static bool CheckWdacBlockSafe()
        {
            try
            {
                return CheckWdacBlock();
            }
            catch (System.Exception ex) when (IsWdacBlockException(ex))
            {
                // FileLoadException with HRESULT 0x800711C7 indicates WDAC policy blocked dynamic DLL loading.
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Even when CUDA Toolkit (nvcuda.dll) is installed, cuInit(0) returns error codes like
        // CudaErrorNoDevice (100) if no physical NVIDIA GPU unit is present or driver fails to communicate.
        // Therefore, we must inspect the return code rather than merely catching DllNotFoundException.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool CheckCudaUnavailable()
        {
#if GSHARP_CUDA
            var result = Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuInit(0);
            if (result != Kassyi.GSharp.Runtime.Interop.CUresult.CudaSuccess)
            {
                return true;
            }
#endif
            return false;
        }

        private static bool CheckCudaUnavailableSafe()
        {
            try
            {
                return CheckCudaUnavailable();
            }
            catch (System.DllNotFoundException)
            {
                return true;
            }
            catch (System.Exception ex) when (IsWdacBlockException(ex))
            {
                return true;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsWdacBlockException(System.Exception ex)
        {
            if (ex is System.IO.FileLoadException fle && (uint)fle.HResult == 0x800711C7) return true;
            if (ex.InnerException is System.IO.FileLoadException innerFle && (uint)innerFle.HResult == 0x800711C7) return true;
            return false;
        }

        // Keep runtime-dependent type references isolated in NoInlining methods
        // so that JIT-time assembly resolution failures (FileLoadException under WDAC)
        // do not crash the caller before reaching the try-catch block.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void EnsureExecutionCapabilityInternal()
        {
#if GSHARP_CUDA
            Skip.IfNot(Kassyi.GSharp.Runtime.Jit.NvccCompiler.IsNvccAvailable, "CUDA Toolkit (NVCC/NVRTC) is not available on this machine.");
            Skip.If(s_isCudaUnavailable.Value, "NVIDIA GPU driver (nvcuda.dll) or GPU device is not available on this machine.");
#else
            Skip.IfNot(Kassyi.GSharp.Runtime.Jit.NvccCompiler.IsMsvcAvailable, "MSVC is not available on this machine.");
            Skip.If(s_isWdacBlocked.Value, "Execution is blocked by Windows Defender Application Control (WDAC).");
#endif
        }

        public static void EnsureExecutionCapability()
        {
            try
            {
                EnsureExecutionCapabilityInternal();
            }
            catch (System.Exception ex) when (IsWdacBlockException(ex))
            {
                Skip.If(true, "Execution is blocked by Windows Defender Application Control (WDAC).");
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void EnsureGpuExecutionCapabilityInternal()
        {
#if GSHARP_CUDA
            Skip.IfNot(Kassyi.GSharp.Runtime.Jit.NvccCompiler.IsNvccAvailable, "CUDA Toolkit (NVCC/NVRTC) is not available on this machine.");
            Skip.If(s_isCudaUnavailable.Value, "NVIDIA GPU driver (nvcuda.dll) or GPU device is not available on this machine.");
#else
            Skip.IfNot(false, "This test requires a GPU execution engine (CUDA) and cannot run in CPU mode.");
#endif
        }

        public static void EnsureGpuExecutionCapability()
        {
            try
            {
                EnsureGpuExecutionCapabilityInternal();
            }
            catch (System.Exception ex) when (IsWdacBlockException(ex))
            {
                Skip.If(true, "Execution is blocked by Windows Defender Application Control (WDAC).");
            }
        }
    }
}
