using System.Buffers.Binary;
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

    private readonly struct Hash256 : IEquatable<Hash256>
    {
        public readonly ulong A, B, C, D;

        public Hash256(ReadOnlySpan<byte> span)
        {
            if (span.Length < 32)
            {
                throw new ArgumentException("Span too short for Hash256", nameof(span));
            }

            A = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(0, 8));
            B = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(8, 8));
            C = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(16, 8));
            D = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(24, 8));
        }

        public static implicit operator Hash256(string _) => default;

        public bool Equals(Hash256 other) => A == other.A && B == other.B && C == other.C && D == other.D;

        public override bool Equals(object? obj) => obj is Hash256 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(A, B, C, D);
    }

    // AOT Cache uses Hash256 and KernelName to prevent collisions and avoid allocations
    private static readonly ConcurrentDictionary<(Hash256 Hash, string KernelName), Lazy<GpuModule>> s_functionCache =
        new();

#pragma warning disable IDE0051 // Justification: Public API for library
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static void Shutdown()
    {
        using var scope = new CudaContextScope();

        foreach (var lazyModule in s_functionCache.Values)
        {
            if (!lazyModule.IsValueCreated)
            {
                continue;
            }

            var module = lazyModule.Value.Module;
            CudaDriverApi.cuModuleUnload(module);
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
        var expectedHashStruct = new Hash256(embeddedHashBytes);
        var cacheKey = (expectedHashStruct, kernelName);

        if (s_functionCache.TryGetValue(cacheKey, out var lazyHit))
        {
            return lazyHit.Value.Function;
        }

        var expectedHashString = Convert.ToHexStringLower(embeddedHashBytes);
        byte[] processed = [.. aotFatbin.Slice(0, aotFatbin.Length - 32)];

        // 1. Check if it's a valid plain text binary first.
        // This prevents a rare edge case where a plain text binary coincidentally starts with "GSHPENC1".
        var plainHash = Convert.ToHexStringLower(SHA256.HashData(processed));
        var isPlainText = string.Equals(plainHash, expectedHashString, StringComparison.OrdinalIgnoreCase);

        if (!isPlainText)
        {
            // 2. If plain hash doesn't match, check if it's encrypted
            var isEncrypted = processed.Length > 8 && Encoding.ASCII.GetString(processed, 0, 8) == "GSHPENC1";

            if (isEncrypted)
            {
                if (decryptionKey == null || decryptionKey.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"AOT kernel binary for '{kernelName}' is encrypted but no decryption key was provided.");
                }

                using var aes = Aes.Create();
                aes.Key = decryptionKey;

                var iv = new byte[16];
                Array.Copy(processed, 8, iv, 0, 16);
                aes.IV = iv;

                using var decryptor = aes.CreateDecryptor();
                using var ms = new MemoryStream(processed, 24, processed.Length - 24);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var output = new MemoryStream();
                cs.CopyTo(output);
                processed = output.ToArray();

                // 3. Hash Validation for decrypted data
                var decryptedHash = Convert.ToHexStringLower(SHA256.HashData(processed));
                if (!string.Equals(decryptedHash, expectedHashString, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"AOT Binary hash mismatch after decryption. Expected: {expectedHashString}");
                }
            }
            else
            {
                // Not encrypted and plain hash mismatch
                throw new InvalidOperationException($"AOT Binary hash mismatch. Expected: {expectedHashString}");
            }
        }

        var lazyModule = s_functionCache.GetOrAdd(cacheKey, _ => new Lazy<GpuModule>(() =>
        {
            using var scope = new CudaContextScope();
            return LoadKernelFunction(processed, kernelName);
        }));

        return lazyModule.Value.Function;
    }

    private static GpuModule LoadKernelFunction(string ptxCode, string kernelName)
    {
        EnsureInitialized();

        // Use the kernel name as the hash for raw PTX strings to keep it simple, or an empty string.
        var cacheKey = ("", kernelName);

        if (s_functionCache.TryGetValue(cacheKey, out var lazyHit))
        {
            return lazyHit.Value;
        }

        var lazyModule = s_functionCache.GetOrAdd(cacheKey, _ => new Lazy<GpuModule>(() =>
        {
            using var scope = new CudaContextScope();
            var moduleData = Encoding.UTF8.GetBytes(ptxCode + "\0");

            // Re-use internal loading logic. For JIT compilation from PTX, we NEED the error log buffer!
            // Wait, we need to pass options here!
            return LoadKernelFunctionPtx(moduleData, kernelName);
        }));

        return lazyModule.Value;
    }

    private static unsafe GpuModule LoadKernelFunctionPtx(byte[] binaryData, string kernelName)
    {
        var errorLogBuffer = new byte[8192];
        void* pAlignedBinary = NativeMemory.AlignedAlloc((nuint)binaryData.Length, 8);
        try
        {
            Marshal.Copy(binaryData, 0, (IntPtr)pAlignedBinary, binaryData.Length);

            fixed (byte* pError = errorLogBuffer)
            {
                nint errorSize = errorLogBuffer.Length;
                IntPtr[] options = [5, 6]; // CU_JIT_ERROR_LOG_BUFFER, SIZE
                IntPtr[] optionValues = [(IntPtr)pError, (IntPtr)(&errorSize)];

                var loadResult = CudaDriverApi.cuModuleLoadDataEx(out var module, (IntPtr)pAlignedBinary,
                    (uint)options.Length, options, optionValues);
                if (loadResult != CUresult.CudaSuccess)
                {
                    var errorLog = Encoding.UTF8.GetString(errorLogBuffer).TrimEnd('\0');
                    throw new CudaException(loadResult, $"cuModuleLoadDataEx failed with {loadResult}:\nError: {errorLog}");
                }

                CudaException.ThrowIfError(CudaDriverApi.cuModuleGetFunction(out var hfunc, module, kernelName));
                return new GpuModule(module, hfunc);
            }
        }
        finally
        {
            NativeMemory.AlignedFree(pAlignedBinary);
        }
    }

    private static unsafe GpuModule LoadKernelFunction(byte[] binaryData, string kernelName)
    {
        // Fatbins MUST be 8-byte aligned for cuModuleLoadDataEx
        void* pAlignedBinary = NativeMemory.AlignedAlloc((nuint)binaryData.Length, 8);
        try
        {
            Marshal.Copy(binaryData, 0, (IntPtr)pAlignedBinary, binaryData.Length);

            // Do NOT pass JIT options (like ERROR_LOG_BUFFER) when loading an AOT Fatbin,
            // as it causes CUDA_ERROR_INVALID_VALUE when the driver simply loads a matching CUBIN without JITing.
            var loadResult = CudaDriverApi.cuModuleLoadDataEx(out var module, (IntPtr)pAlignedBinary, 0, null, null);
            if (loadResult != CUresult.CudaSuccess)
            {
                throw new CudaException(loadResult, $"cuModuleLoadDataEx failed with {loadResult} for kernel '{kernelName}'.");
            }

            CudaException.ThrowIfError(CudaDriverApi.cuModuleGetFunction(out var hfunc, module, kernelName));
            return new GpuModule(module, hfunc);
        }
        finally
        {
            NativeMemory.AlignedFree(pAlignedBinary);
        }
    }
}
