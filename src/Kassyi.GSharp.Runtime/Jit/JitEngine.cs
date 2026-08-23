// ReSharper disable UnusedMember.Global

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Runtime.Jit;

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
            if (!s_isGlobalInit)
            {
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
            if (kvp.Value.IsValueCreated)
            {
                try
                {
                    CudaDriverApi.cuModuleUnload(kvp.Value.Value.Module);
                }
                catch
                {
                    // Ignore unload errors during shutdown to ensure we attempt to unload all modules.
                }
            }
        }

        s_functionCache.Clear();
    }

    /// <summary>
    /// JIT compiles the given CUDA C++ source string into a Fatbin, loads it into the current context,
    /// and returns the handle to the specified kernel function.
    /// Results are cached based on the hash of the source code.
    /// </summary>
    public static IntPtr GetOrCompileKernel(string cppSource, string kernelName, Dictionary<string, string>? macros = null)
    {
        if (macros != null && macros.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var kvp in macros)
            {
                sb.AppendLine($"#define {kvp.Key} {kvp.Value}");
            }
            cppSource = sb.ToString() + cppSource;
        }

        var sourceHash = ComputeHash(cppSource);
        var cacheKey = $"{sourceHash}_{s_smVersion}_{kernelName}";

        var lazyModule = s_functionCache.GetOrAdd(cacheKey, _ => new Lazy<GpuModule>(() =>
        {
            using var scope = new CudaContextScope();

            // Compilation path
            var tempDir = Path.Combine(Path.GetTempPath(), "Kassyi.GSharp.Runtime.JitCache");
            Directory.CreateDirectory(tempDir);

            var cuFile = Path.Combine(tempDir, $"{sourceHash}.cu");
            var fatbinFile = Path.Combine(tempDir, $"{sourceHash}_{s_smVersion}.fatbin");

            if (!File.Exists(fatbinFile))
            {
                File.WriteAllText(cuFile, cppSource, new UTF8Encoding(false));

                // Invoke the compiler backend with SM version
                NvccCompiler.CompileToFatbin(cuFile, fatbinFile, null, tempDir, s_smVersion);
            }

            var ptxBytes = File.ReadAllBytes(fatbinFile);

            CudaException.ThrowIfError(CudaDriverApi.cuModuleLoadDataEx(out var module, ptxBytes, 0, null, null));
            CudaException.ThrowIfError(CudaDriverApi.cuModuleGetFunction(out var hfunc, module, kernelName));

            return new GpuModule(module, hfunc);
        }));

        return lazyModule.Value.Function;
    }

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hashBytes);
    }
}
