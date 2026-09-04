using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Kassyi.GSharp.Runtime.Jit;

/// <summary>
/// Runtime JIT compilation engine that compiles Standard C++ kernel strings into executable native CPU libraries and manages delegate bindings.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for runtime consumers")]
public sealed class CpuJitEngine
{
    private static readonly string s_cacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kassyi", "GSharp", "Cache", "CpuDlls"
    );

    // Cache for loaded delegates: Hash of Source -> Lazy<Delegate>
    private static readonly ConcurrentDictionary<string, Lazy<Delegate>> s_functionCache = new();

    // Maintain a list of loaded module handles to free during Shutdown
    private static readonly ConcurrentBag<IntPtr> s_loadedModules = new();

    /// <summary>
    /// Unloads all cached CPU modules and clears the in-memory cache to reclaim memory.
    /// </summary>
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Public API for library")]
    public static void Shutdown()
    {
        s_functionCache.Clear();

        foreach (var module in s_loadedModules)
        {
            try
            {
                NativeLibrary.Free(module);
            }
            catch
            {
                // Ignore unload errors during shutdown
            }
        }
        s_loadedModules.Clear();
    }

    /// <summary>
    /// JIT-compiles Standard C++ source text into a native DLL, loads it, and returns a raw function pointer.
    /// </summary>
    public static IntPtr GetOrCompileKernelPtr(string kernelName, string baseSource,
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

        sb.Append($"|src={ComputeHash(baseSource)}");
        var cacheKey = sb.ToString();

        var lazyPtr = s_functionCache.GetOrAdd(cacheKey, _ => new Lazy<Delegate>(() =>
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

            var cacheFileName = $"kernel_{sourceHash}_{Environment.ProcessId}.dll";
            var dllPath = Path.Combine(s_cacheDirectory, cacheFileName);

            if (!File.Exists(dllPath))
            {
                CompileToDll(cppSource, dllPath);
            }

            // We need a dummy delegate to store in the cache, but we just want the pointer
            return new Func<IntPtr>(() => LoadKernelFunctionPtr(dllPath, kernelName));
        }));

        if (lazyPtr.Value is Func<IntPtr> ptrFunc)
        {
            return ptrFunc();
        }

        // If it was already cached as a real delegate by GetOrCompileKernel, we can't easily get the pointer back.
        // But since we only call this for __gsharp_set_thread_idx, it should be fine.
        throw new Exception("Cache type mismatch");
    }

    /// <summary>
    /// JIT-compiles Standard C++ source text into a native DLL, loads it, and returns a strongly-typed delegate.
    /// </summary>
    public static TDelegate GetOrCompileKernel<TDelegate>(string kernelName, string baseSource,
        Dictionary<string, string>? jitConstants = null, Dictionary<string, string>? macros = null) where TDelegate : Delegate
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

        sb.Append($"|src={ComputeHash(baseSource)}");
        var cacheKey = sb.ToString();

        var lazyDelegate = s_functionCache.GetOrAdd(cacheKey, _ => new Lazy<Delegate>(() =>
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

            var cacheFileName = $"kernel_{sourceHash}_{Environment.ProcessId}.dll";
            var dllPath = Path.Combine(s_cacheDirectory, cacheFileName);

            if (!File.Exists(dllPath))
            {
                CompileToDll(cppSource, dllPath);
            }

            return LoadKernelFunction<TDelegate>(dllPath, kernelName);
        }));

        return (TDelegate)lazyDelegate.Value;
    }

    private static string PreprocessSource(string cppSource, Dictionary<string, string>? macros)
    {
        if (macros == null || macros.Count == 0)
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

    private static void CompileToDll(string cppSource, string dllPath)
    {
        try
        {
            if (!Directory.Exists(s_cacheDirectory))
            {
                Directory.CreateDirectory(s_cacheDirectory);
            }
        }
        catch
        {
            // Ignore
        }

        var tempDir = s_cacheDirectory;
        var cppFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}.cpp");
        File.WriteAllText(cppFile, cppSource, new UTF8Encoding(false));

        try
        {
            MsvcCompiler.Compile(cppFile, dllPath, null, tempDir);
        }
        finally
        {
            try { File.Delete(cppFile); } catch { /* Ignore */ }
        }
    }

    private static IntPtr LoadKernelFunctionPtr(string dllPath, string kernelName)
    {
        var module = NativeLibrary.Load(dllPath);
        s_loadedModules.Add(module);

        if (!NativeLibrary.TryGetExport(module, kernelName, out var hfunc))
        {
            throw new Exception($"Failed to find exported function '{kernelName}' in {dllPath}. Did you forget extern \"C\" __declspec(dllexport)?");
        }

        return hfunc;
    }

    private static TDelegate LoadKernelFunction<TDelegate>(string dllPath, string kernelName) where TDelegate : Delegate => Marshal.GetDelegateForFunctionPointer<TDelegate>(LoadKernelFunctionPtr(dllPath, kernelName));

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hashBytes);
    }
}
