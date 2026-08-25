using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Runtime.Jit;

[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for runtime consumers")]
public static class JitEngine
{
    private static bool s_isGlobalInit;
    private static IntPtr s_globalContext;
    private static string s_smVersion = "sm_52"; // default fallback
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
    /// Unloads all cached modules and clears the cache to prevent device memory leaks.
    /// </summary>
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
    /// JIT compiles the given CUDA C++ source string into a Fatbin, loads it into the current context,
    /// and returns the handle to the specified kernel function.
    /// Results are cached based on the hash of the source code.
    /// </summary>
    public static IntPtr GetOrCompileKernel(string kernelName, string baseSource,
        Dictionary<string, string>? jitConstants = null, Dictionary<string, string>? macros = null)
    {
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

            using var scope = new CudaContextScope();
            var fatbinFile = CompileToFatbin(sourceHash, cppSource);
            return LoadKernelFunction(fatbinFile, kernelName);
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

    private static string CompileToFatbin(string sourceHash, string cppSource)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Kassyi.GSharp.Runtime.JitCache");
        Directory.CreateDirectory(tempDir);

        var cuFile = Path.Combine(tempDir, $"{sourceHash}.cu");
        var fatbinFile = Path.Combine(tempDir, $"{sourceHash}_{s_smVersion}.fatbin");

        if (File.Exists(fatbinFile))
        {
            return fatbinFile;
        }

        File.WriteAllText(cuFile, cppSource, new UTF8Encoding(false));
        NvccCompiler.CompileToFatbin(cuFile, fatbinFile, null, tempDir, s_smVersion);

        return fatbinFile;
    }

    private static GpuModule LoadKernelFunction(string fatbinFile, string kernelName)
    {
        var ptxBytes = File.ReadAllBytes(fatbinFile);
        CudaException.ThrowIfError(CudaDriverApi.cuModuleLoadDataEx(out var module, ptxBytes, 0, null, null));
        CudaException.ThrowIfError(CudaDriverApi.cuModuleGetFunction(out var hfunc, module, kernelName));
        return new GpuModule(module, hfunc);
    }

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hashBytes);
    }

    public static void SynchronizeDevice()
    {
        using var scope = new CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
    }
}
