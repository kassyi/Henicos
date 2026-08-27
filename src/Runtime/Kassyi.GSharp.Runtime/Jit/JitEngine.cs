using System.Security.Cryptography;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Runtime.Jit;

/// <summary>
/// Runtime JIT compilation engine that compiles CUDA C++ kernel strings into executable GPU modules and manages thread contexts.
/// </summary>
/// <remarks>
/// <para>Kernels are compiled via NVRTC on-demand and cached in a two-tier cache (in-memory <see cref="ConcurrentDictionary{TKey,TValue}"/> and disk-based PTX cache) to eliminate redundant compilation overhead.</para>
/// <para><b>Thread Safety:</b> All public methods except <see cref="Shutdown"/> are safe for concurrent invocation from multiple threads.</para>
/// </remarks>
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for runtime consumers")]
public sealed class JitEngine
{
    private static volatile bool s_isGlobalInit;
    private static IntPtr s_globalContext;
    private static string s_smVersion = "sm_52"; // default fallback
    private static readonly Lock s_initLock = new();

    private static readonly string s_cacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kassyi", "GSharp", "Cache", "Ptx"
    );

    /// <summary>
    /// An RAII struct that binds the global GSharp CUDA context to the current thread and restores the previous context upon disposal.
    /// </summary>
    public readonly struct CudaContextScope : IDisposable
    {
        private readonly IntPtr _prevContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="CudaContextScope"/> struct, switching the thread's CUDA context to GSharp global context.
        /// </summary>
        public CudaContextScope()
        {
            EnsureInitialized();
            CudaDriverApi.cuCtxGetCurrent(out _prevContext);
            if (_prevContext != s_globalContext)
            {
                CudaException.ThrowIfError(CudaDriverApi.cuCtxSetCurrent(s_globalContext));
            }
        }

        /// <summary>
        /// Restores the previously active CUDA context on the current thread.
        /// </summary>
        public void Dispose()
        {
            if (_prevContext != s_globalContext)
            {
                CudaDriverApi.cuCtxSetCurrent(_prevContext);
            }
        }
    }

    // ReSharper disable once InconsistentlySynchronizedField
    private static void EnsureInitialized()
    {
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

            CudaException.ThrowIfError(CudaDriverApi.cuDeviceGetAttribute(out var major,
                CudaDriverApi.CuDeviceAttribute.ComputeCapabilityMajor, device));
            CudaException.ThrowIfError(CudaDriverApi.cuDeviceGetAttribute(out var minor,
                CudaDriverApi.CuDeviceAttribute.ComputeCapabilityMinor, device));
            s_smVersion = $"sm_{major}{minor}";

            CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out s_globalContext, 0, device));
            s_isGlobalInit = true;
        }
    }

    private readonly struct GpuModule(IntPtr module, IntPtr function)
    {
        public IntPtr Module { get; } = module;
        public IntPtr Function { get; } = function;
    }

    // Cache for compiled functions: Hash of Source -> Lazy<GpuModule>
    private static readonly ConcurrentDictionary<string, Lazy<GpuModule>> s_functionCache = new();

    /// <summary>
    /// Unloads all cached GPU modules from the device driver and clears the in-memory cache to reclaim device memory.
    /// </summary>
    /// <remarks>
    /// <para>Safe to call during application teardown or when changing GPU runtime contexts.</para>
    /// <para><b>Thread Safety:</b> This method is NOT safe to call concurrently with other engine operations.</para>
    /// </remarks>
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Public API for library")]
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
                // Ignore unload errors during shutdown to ensure we attempt to unload all modules.
            }
        }

        s_functionCache.Clear();
    }

    /// <summary>
    /// JIT-compiles CUDA C++ source text into PTX, loads the module into the active CUDA context, and returns the function handle (<c>CUfunction</c>).
    /// </summary>
    /// <param name="kernelName">The name of the <c>__global__</c> kernel function to locate and execute.</param>
    /// <param name="baseSource">The CUDA C++ source code text.</param>
    /// <param name="jitConstants">Optional compile-time constant key-value pairs to inject as preprocessor macros.</param>
    /// <param name="macros">Optional preprocessor macro definitions.</param>
    /// <returns>A native pointer handle (<see cref="IntPtr"/>) representing the loaded <c>CUfunction</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="kernelName"/> or <paramref name="baseSource"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exception">Thrown when JIT compilation or driver module loading fails.</exception>
    /// <remarks>
    /// <para><b>Caching:</b> Compilation results are cached in-memory and on disk. Identical source text, constants, and SM architectures return the cached module without invoking NVRTC.</para>
    /// <para><b>Thread Safety:</b> Thread-safe. Compilation is lazily executed once per unique cache key.</para>
    /// </remarks>
    public static IntPtr GetOrCompileKernel(string kernelName, string baseSource,
        Dictionary<string, string>? jitConstants = null, Dictionary<string, string>? macros = null)
    {
        EnsureInitialized();

        var sb = new StringBuilder(kernelName);
        if (jitConstants != null)
        {
            foreach (var kvp in jitConstants.OrderBy(x => x.Key))
            {
                sb.Append($"|{kvp.Key}={kvp.Value}");
            }
        }

        if (macros != null)
        {
            foreach (var kvp in macros.OrderBy(x => x.Key))
            {
                sb.Append($"|{kvp.Key}={kvp.Value}");
            }
        }

        var cacheKey = sb.ToString();

        var lazyModule = s_functionCache.GetOrAdd(cacheKey, _ => new Lazy<GpuModule>(() =>
        {
            var combinedMacros = new Dictionary<string, string>();
            if (jitConstants != null)
            {
                foreach (var kvp in jitConstants)
                {
                    combinedMacros[kvp.Key] = kvp.Value;
                }
            }

            if (macros != null)
            {
                foreach (var kvp in macros)
                {
                    combinedMacros[kvp.Key] = kvp.Value;
                }
            }

            var cppSource = PreprocessSource(baseSource, combinedMacros);
            var sourceHash = ComputeHash(cppSource);
            var toolkitHash = ComputeHash(GetCudaToolkitPath());
            var nvrtcVersion = NvrtcApi.GetNvrtcVersion();

            var cacheFileName = $"{kernelName}_{sourceHash}_{s_smVersion}_{nvrtcVersion}_{toolkitHash}.ptx";
            var cacheFilePath = Path.Combine(s_cacheDirectory, cacheFileName);
            byte[]? ptxBytes = null;

            if (File.Exists(cacheFilePath))
            {
                try
                {
                    var loadedBytes = File.ReadAllBytes(cacheFilePath);
                    if (loadedBytes.Length > 0)
                    {
                        ptxBytes = loadedBytes;
                    }
                    else
                    {
                        TryDeleteFile(cacheFilePath);
                    }
                }
                catch
                {
                    // Ignore read errors and fallback to fresh compilation
                    TryDeleteFile(cacheFilePath);
                }
            }

            using var scope = new CudaContextScope();

            if (ptxBytes != null)
            {
                try
                {
                    return LoadKernelFunction(ptxBytes, kernelName);
                }
                catch
                {
                    // Cached PTX is invalid or corrupt. Invalidate cache and fallback to recompile.
                    TryDeleteFile(cacheFilePath);
                }
            }

            ptxBytes = CompileToPtx(cppSource, kernelName);
            WriteCacheFileAtomic(cacheFilePath, ptxBytes);
            return LoadKernelFunction(ptxBytes, kernelName);
        }));

        return lazyModule.Value.Function;
    }

    private static string PreprocessSource(string cppSource, Dictionary<string, string>? macros)
    {
        if (macros is not { Count: > 0 })
        {
            return cppSource;
        }

        var sb = new StringBuilder();
        foreach (var kvp in macros)
        {
            sb.AppendLine($"#define {kvp.Key} {kvp.Value}");
        }

        return sb + cppSource;
    }

    private static byte[] CompileToPtx(string cppSource, string kernelName)
    {
        var options = new List<string>
        {
            $"-arch={s_smVersion}",
            "--std=c++17",
            "-DCCCL_IGNORE_MSVC_TRADITIONAL_PREPROCESSOR_WARNING"
        };

        var cudaPath = GetCudaToolkitPath();
        if (!string.IsNullOrEmpty(cudaPath))
        {
            addIncludePaths(cudaPath);
        }

        return NvrtcCompiler.CompileToPtx(cppSource, kernelName, [.. options]);

        void addIncludePaths(string baseDir)
        {
            var includePath = Path.Combine(baseDir, "include");
            if (!Directory.Exists(includePath))
            {
                return;
            }

            options.Add($"-I{includePath}");

            var ccclPath = Path.Combine(includePath, "cccl");
            if (!Directory.Exists(ccclPath))
            {
                return;
            }

            options.Add($"-I{ccclPath}");

            var libcxxPath = Path.Combine(ccclPath, "libcxx", "include");
            if (Directory.Exists(libcxxPath))
            {
                options.Add($"-I{libcxxPath}");
            }
        }
    }

    private static string GetCudaToolkitPath()
    {
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrEmpty(cudaPath))
        {
            return cudaPath;
        }

        var cudaBaseDir = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
        if (!Directory.Exists(cudaBaseDir))
        {
            return string.Empty;
        }

        foreach (var dir in Directory.GetDirectories(cudaBaseDir).OrderByDescending(d => d))
        {
            if (Directory.Exists(Path.Combine(dir, "include")))
            {
                return dir;
            }
        }

        return string.Empty;
    }

    private static unsafe GpuModule LoadKernelFunction(byte[] ptxBytes, string kernelName)
    {
        var errorLogBuffer = new byte[8192];
        var infoLogBuffer = new byte[8192];

        fixed (byte* pError = errorLogBuffer, pInfo = infoLogBuffer)
        {
            IntPtr[] options =
            [
                3, // CU_JIT_INFO_LOG_BUFFER
                4, // CU_JIT_INFO_LOG_BUFFER_SIZE_BYTES
                5, // CU_JIT_ERROR_LOG_BUFFER
                6 // CU_JIT_ERROR_LOG_BUFFER_SIZE_BYTES
            ];

            IntPtr[] optionValues =
            [
                (IntPtr)pInfo,
                infoLogBuffer.Length,
                (IntPtr)pError,
                errorLogBuffer.Length
            ];

            var loadResult = CudaDriverApi.cuModuleLoadDataEx(out var module, ptxBytes, (uint)options.Length, options,
                optionValues);
            if (loadResult != CUresult.CudaSuccess)
            {
                var errorLog = Encoding.UTF8.GetString(errorLogBuffer).TrimEnd('\0');
                var infoLog = Encoding.UTF8.GetString(infoLogBuffer).TrimEnd('\0');
                var ptxStr = Encoding.UTF8.GetString(ptxBytes).TrimEnd('\0');
                Logger<JitEngine>.Info(
                    $"[CUDA JIT ERROR]: {loadResult}\nInfo Log:\n{infoLog}\nError Log:\n{errorLog}\nPTX Source:\n{ptxStr}");
                throw new Exception(
                    $"cuModuleLoadDataEx failed with {loadResult}:\nInfo: {infoLog}\nError: {errorLog}");
            }

            CudaException.ThrowIfError(CudaDriverApi.cuModuleGetFunction(out var hfunc, module, kernelName));
            return new GpuModule(module, hfunc);
        }
    }

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hashBytes);
    }

    /// <summary>
    /// Blocks the CPU host thread until all previously issued GPU operations in the active context complete.
    /// </summary>
    /// <exception cref="CudaException">Thrown when device synchronization returns an error code.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> This is a blocking call and forces CPU-GPU synchronization, which degrades parallel execution efficiency.</para>
    /// </remarks>
    public static void SynchronizeDevice()
    {
        using var scope = new CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
    }

    private static void WriteCacheFileAtomic(string targetPath, byte[] content)
    {
        string? tempFilePath = null;
        try
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            tempFilePath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(tempFilePath, content);
            File.Move(tempFilePath, targetPath, overwrite: true);
        }
        catch
        {
            // Ignore cache write errors on best-effort basis to avoid crashing execution
        }
        finally
        {
            if (tempFilePath != null)
            {
                TryDeleteFile(tempFilePath);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup errors on best-effort basis
        }
    }
}
