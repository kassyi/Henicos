using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Runtime AOT compilation engine that manages thread contexts and loads executable GPU modules.
/// </summary>
// ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
public sealed class GpuKernelLoader
{
    private static volatile bool s_isGlobalInit;
    private static IntPtr s_globalContext;
    private static readonly Lock s_initLock = new();

    public readonly struct CudaContextScope : IDisposable
    {
        private readonly IntPtr _prevContext;

        public CudaContextScope()
        {
            EnsureInitialized();
            CudaDriverApi.cuCtxGetCurrent(out _prevContext);
            if (_prevContext != s_globalContext)
            {
                CudaException.ThrowIfError(CudaDriverApi.cuCtxSetCurrent(s_globalContext));
            }
        }

        public void Dispose()
        {
            if (_prevContext != s_globalContext)
            {
                CudaDriverApi.cuCtxSetCurrent(_prevContext);
            }
        }
    }

    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static bool IsDriverAvailable
    {
        get
        {
            try
            {
                EnsureInitialized();
                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (CudaException)
            {
                return false;
            }
        }
    }

    private static void EnsureInitialized()
    {
        // ReSharper disable once InconsistentlySynchronizedField -- Double-checked locking pattern
        if (s_isGlobalInit)
        {
            return;
        }

        lock (s_initLock)
        {
            if (s_isGlobalInit)
            {
                return;
            }

            CudaException.ThrowIfError(CudaDriverApi.cuInit(0));
            CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out var device, 0));

            CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out s_globalContext, 0, device));
            s_isGlobalInit = true;
        }
    }

    // Track loaded modules for Shutdown cleanup
    private static readonly ConcurrentBag<IntPtr> s_loadedModules = [];

    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static int Generation { get; private set; }

#pragma warning disable IDE0051 // Justification: Public API for library
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static void Shutdown()
    {
        using var scope = new CudaContextScope();

        foreach (var module in s_loadedModules)
        {
            CudaDriverApi.cuModuleUnload(module);
        }

        s_loadedModules.Clear();
        Generation++;
    }

    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static void SynchronizeDevice()
    {
        using var scope = new CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
    }

    /// <summary>
    /// Loads an Ahead-Of-Time (AOT) compiled Fatbin directly into the CUDA driver.
    /// </summary>
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static IntPtr LoadModule(ReadOnlySpan<byte> aotFatbin)
    {
        EnsureInitialized();

        if (aotFatbin.Length < 32)
        {
            throw new InvalidOperationException("AOT kernel binary is too small to contain a valid hash.");
        }

        var embeddedHashBytes = aotFatbin.Slice(aotFatbin.Length - 32);
        var expectedHashString = Convert.ToHexStringLower(embeddedHashBytes);
        byte[] processed = [.. aotFatbin.Slice(0, aotFatbin.Length - 32)];

        var actualHashString = Convert.ToHexStringLower(SHA256.HashData(processed));
        if (!string.Equals(actualHashString, expectedHashString, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"AOT Binary hash mismatch. The file may be corrupted or tampered with. Expected: {expectedHashString}");
        }

        using var scope = new CudaContextScope();

        unsafe
        {
            // Fatbins MUST be 8-byte aligned for cuModuleLoadDataEx
            void* pAlignedBinary = NativeMemory.AlignedAlloc((nuint)processed.Length, 8);
            try
            {
                Marshal.Copy(processed, 0, (IntPtr)pAlignedBinary, processed.Length);

                // Do NOT pass JIT options (like ERROR_LOG_BUFFER) when loading an AOT Fatbin,
                // as it causes CUDA_ERROR_INVALID_VALUE when the driver simply loads a matching CUBIN without JITing.
                var loadResult = CudaDriverApi.cuModuleLoadDataEx(out var module, (IntPtr)pAlignedBinary, 0, null, null);
                if (loadResult != CUresult.CudaSuccess)
                {
                    throw new CudaException(loadResult, $"cuModuleLoadDataEx failed with {loadResult}.");
                }

                s_loadedModules.Add(module);
                return module;
            }
            finally
            {
                NativeMemory.AlignedFree(pAlignedBinary);
            }
        }
    }

    /// <summary>
    /// Retrieves a function pointer from a loaded CUDA module.
    /// </summary>
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static IntPtr GetFunction(IntPtr hModule, string kernelName)
    {
        EnsureInitialized();
        using var scope = new CudaContextScope();
        
        CudaException.ThrowIfError(CudaDriverApi.cuModuleGetFunction(out var hfunc, hModule, kernelName));
        return hfunc;
    }
}
