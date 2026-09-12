using System.Security.Cryptography;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Runtime JIT/AOT compilation engine that manages thread contexts and loads executable GPU modules.
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

    private readonly struct GpuModule(IntPtr module, IntPtr function)
    {
        public IntPtr Module { get; } = module;
        public IntPtr Function { get; } = function;
    }

    // AOT Cache uses KernelName as the key since kernel names are unique
    private static readonly ConcurrentDictionary<string, Lazy<GpuModule>> s_functionCache = new();

#pragma warning disable IDE0051 // Justification: Public API for library
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static void Shutdown()
    {
        using var scope = new CudaContextScope();

        foreach (var kvp in s_functionCache)
        {
            if (!kvp.Value.IsValueCreated)
            {
                continue;
            }

            try
            {
                CudaDriverApi.cuModuleUnload(kvp.Value.Value.Module);
            }
            catch
            {
                /* Ignore */
            }
        }

        s_functionCache.Clear();
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
    public static IntPtr LoadAotKernel(ReadOnlySpan<byte> aotFatbin, string kernelName,
        byte[]? decryptionKey = null)
    {
        EnsureInitialized();

        if (aotFatbin.Length < 32)
        {
            throw new InvalidOperationException("AOT kernel binary is too small to contain a valid hash.");
        }

        var embeddedHashBytes = aotFatbin.Slice(aotFatbin.Length - 32);
        var expectedHash = Convert.ToHexStringLower(embeddedHashBytes);
        var cacheKey = $"{expectedHash}::{kernelName}";

        if (s_functionCache.TryGetValue(cacheKey, out var lazyHit))
        {
            return lazyHit.Value.Function;
        }

        byte[] processed = [.. aotFatbin.Slice(0, aotFatbin.Length - 32)];
        var isEncrypted = processed.Length > 8 && Encoding.ASCII.GetString(processed, 0, 8) == "GSHPENC1";

        // 1. Decryption if required
        if (isEncrypted)
        {
            if (decryptionKey == null || decryptionKey.Length == 0)
            {
                throw new InvalidOperationException("AOT kernel is encrypted but no decryption key was provided.");
            }

            using var aes = Aes.Create();
            aes.Key = decryptionKey.Length == 32 ? decryptionKey : SHA256.HashData(decryptionKey);

            var iv = new byte[16];
            Array.Copy(processed, 8, iv, 0, 16);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(processed, 24, processed.Length - 24);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var output = new MemoryStream();
            cs.CopyTo(output);
            processed = output.ToArray();
        }

        // 2. Hash Validation
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(processed));
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"AOT Binary hash mismatch. Expected: {expectedHash}");
        }

        var lazyModule = s_functionCache.GetOrAdd(cacheKey, _ => new Lazy<GpuModule>(() =>
        {
            using var scope = new CudaContextScope();
            return LoadKernelFunction(processed, kernelName);
        }));

        return lazyModule.Value.Function;
    }

    private static unsafe GpuModule LoadKernelFunction(byte[] binaryData, string kernelName)
    {
        var errorLogBuffer = new byte[8192];
        fixed (byte* pError = errorLogBuffer)
        {
            IntPtr[] options = [5, 6]; // CU_JIT_ERROR_LOG_BUFFER, SIZE
            IntPtr[] optionValues = [(IntPtr)pError, errorLogBuffer.Length];

            var loadResult = CudaDriverApi.cuModuleLoadDataEx(out var module, binaryData, (uint)options.Length, options,
                optionValues);
            if (loadResult != CUresult.CudaSuccess)
            {
                var errorLog = Encoding.UTF8.GetString(errorLogBuffer).TrimEnd('\0');
                throw new Exception($"cuModuleLoadDataEx failed with {loadResult}:\nError: {errorLog}");
            }

            CudaException.ThrowIfError(CudaDriverApi.cuModuleGetFunction(out var hfunc, module, kernelName));
            return new GpuModule(module, hfunc);
        }
    }
}
