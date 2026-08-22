// ReSharper disable UnusedMember.Global

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Runtime.Transpiler.Pipeline;

public static class JitEngine
{
    private static bool s_isGlobalInit;
    private static IntPtr s_globalContext;
    private static string s_smVersion = "sm_52"; // default fallback
    private static readonly Lock s_initLock = new();

    [ThreadStatic]
    private static bool s_isThreadBound;

    private readonly struct GpuModule(IntPtr module, IntPtr function)
    {
        public IntPtr Module { get; } = module;
        public IntPtr Function { get; } = function;
    }

    // Cache for compiled functions: Hash of Source -> GpuModule
    private static readonly ConcurrentDictionary<string, GpuModule> s_functionCache = new();

    private static void EnsureInitialized()
    {
        if (s_isThreadBound)
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

        // Bind the global context to the current thread
        CudaException.ThrowIfError(CudaDriverApi.cuCtxSetCurrent(s_globalContext));
        s_isThreadBound = true;
    }

    /// <summary>
    /// Unloads all cached modules and clears the cache to prevent device memory leaks.
    /// </summary>
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Public API for library")]
    public static void Shutdown()
    {
        foreach (var kvp in s_functionCache)
        {
            try
            {
                CudaDriverApi.cuModuleUnload(kvp.Value.Module);
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
    public static IntPtr GetOrCompileKernel(string cppSource, string kernelName)
    {
        EnsureInitialized();

        var sourceHash = ComputeHash(cppSource);
        var cacheKey = $"{sourceHash}_{s_smVersion}_{kernelName}";

        if (s_functionCache.TryGetValue(cacheKey, out var cachedModule))
        {
            return cachedModule.Function;
        }

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

        var gpuModule = new GpuModule(module, hfunc);
        s_functionCache[cacheKey] = gpuModule;

        return gpuModule.Function;
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = sha256.ComputeHash(bytes);
        var sb = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }
}
