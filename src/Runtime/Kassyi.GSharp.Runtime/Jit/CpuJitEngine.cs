using System.Buffers;
using System.Security.Cryptography;

namespace Kassyi.GSharp.Runtime.Jit;

/// <summary>
/// Runtime JIT compilation engine that compiles Standard C++ kernel strings into executable native CPU libraries and manages delegate bindings.
/// </summary>
// ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
public sealed class CpuJitEngine
{
    private static readonly string s_cacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kassyi", "GSharp", "Cache", "CpuDlls"
    );

    // Cache for loaded delegates: Hash of Source and Args -> Lazy<Delegate>
    private static readonly ConcurrentDictionary<Guid, Lazy<Delegate>> s_functionCache = new();

    // Maintain a list of loaded module handles to free during Shutdown
    private static readonly ConcurrentBag<IntPtr> s_loadedModules = new();

    /// <summary>
    /// Unloads all cached CPU modules and clears the in-memory cache to reclaim memory.
    /// </summary>
    #pragma warning disable IDE0051 // Justification: Public API for library
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
        ReadOnlySpan<JitConstantArg> jitConstants = default, ReadOnlySpan<KeyValuePair<string, string>> macros = default)
    {
        var cacheKey = ComputeCacheKey(kernelName, baseSource, jitConstants, macros);

        // Cache miss: Allocate on heap for lambda capture
        var jitArr = jitConstants.ToArray();
        var macroArr = macros.ToArray();

        var lazyPtr = s_functionCache.GetOrAdd(cacheKey, _ => new Lazy<Delegate>(() =>
        {
            var dllPath = CompileKernelInternal(kernelName, baseSource, jitArr, macroArr);
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
        ReadOnlySpan<JitConstantArg> jitConstants = default,
        ReadOnlySpan<KeyValuePair<string, string>> macros = default) where TDelegate : Delegate
    {
        var cacheKey = ComputeCacheKey(kernelName, baseSource, jitConstants, macros);

        if (s_functionCache.TryGetValue(cacheKey, out var lazyHit))
        {
            return (TDelegate)lazyHit.Value;
        }

        // Cache miss: Allocate on heap for lambda capture
        var jitArr = jitConstants.ToArray();
        var macroArr = macros.ToArray();

        var lazyDelegate = s_functionCache.GetOrAdd(cacheKey, _ => new Lazy<Delegate>(() =>
        {
            var dllPath = CompileKernelInternal(kernelName, baseSource, jitArr, macroArr);
            return LoadKernelFunction<TDelegate>(dllPath, kernelName);
        }));

        return (TDelegate)lazyDelegate.Value;
    }

    private static string CompileKernelInternal(string kernelName, string baseSource,
        ReadOnlySpan<JitConstantArg> jitConstants, ReadOnlySpan<KeyValuePair<string, string>> macros)
    {
        var combinedMacros = new Dictionary<string, string>();
        foreach (var c in jitConstants)
        {
            combinedMacros[c.Key] = c.GetStringValue();
        }

        foreach (var m in macros)
        {
            combinedMacros[m.Key] = m.Value;
        }

        var cppSource = PreprocessSource(baseSource, combinedMacros);
        var sourceHash = ComputeHash(cppSource);

        var cacheFileName = $"kernel_{sourceHash}_{Environment.ProcessId}.dll";
        var dllPath = Path.Combine(s_cacheDirectory, cacheFileName);

        if (!File.Exists(dllPath))
        {
            CompileToDll(cppSource, dllPath);
        }

        return dllPath;
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

    private static Guid ComputeCacheKey(string kernelName, string baseSource, ReadOnlySpan<JitConstantArg> jitConstants,
        ReadOnlySpan<KeyValuePair<string, string>> macros)
    {
        var totalLength = kernelName.Length * 2 + baseSource.Length * 2;
        foreach (var c in jitConstants)
        {
            totalLength += c.Key.Length * 2 + 1 + 8;
        }

        foreach (var m in macros)
        {
            totalLength += m.Key.Length * 2 + m.Value.Length * 2;
        }

        byte[]? rentedArray = null;
        var buffer = totalLength <= 4096
            ? stackalloc byte[4096]
            : (rentedArray = ArrayPool<byte>.Shared.Rent(totalLength));

        try
        {
            var offset = 0;

            static void Append(ref Span<byte> buf, ref int off, ReadOnlySpan<byte> data)
            {
                data.CopyTo(buf.Slice(off));
                off += data.Length;
            }

            Append(ref buffer, ref offset, MemoryMarshal.AsBytes(kernelName.AsSpan()));
            Append(ref buffer, ref offset, MemoryMarshal.AsBytes(baseSource.AsSpan()));

            foreach (var c in jitConstants)
            {
                Append(ref buffer, ref offset, MemoryMarshal.AsBytes(c.Key.AsSpan()));
                var typeVal = c.Type;
                Append(ref buffer, ref offset, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref typeVal, 1)));
                var valVal = c.Value;
                Append(ref buffer, ref offset, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref valVal, 1)));
            }

            foreach (var m in macros)
            {
                Append(ref buffer, ref offset, MemoryMarshal.AsBytes(m.Key.AsSpan()));
                Append(ref buffer, ref offset, MemoryMarshal.AsBytes(m.Value.AsSpan()));
            }

            Span<byte> hashBytes = stackalloc byte[32];
            SHA256.HashData(buffer.Slice(0, offset), hashBytes);
            return new Guid(hashBytes.Slice(0, 16));
        }
        finally
        {
            if (rentedArray != null)
            {
                ArrayPool<byte>.Shared.Return(rentedArray);
            }
        }
    }
}
