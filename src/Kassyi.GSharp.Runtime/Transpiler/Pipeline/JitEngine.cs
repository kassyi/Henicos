using System.Security.Cryptography;
using Kassyi.GSharp.Runtime.Core;

namespace Kassyi.GSharp.Runtime.Transpiler.Pipeline;

public static class JitEngine
{
    private static bool _isInitialized;
    private static IntPtr _context;

    // Cache for compiled functions: Hash of Source -> Function Handle
    private static readonly ConcurrentDictionary<string, IntPtr> _functionCache = new();

    private static void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        lock (_functionCache)
        {
            if (_isInitialized)
            {
                return;
            }

            CudaException.ThrowIfError(CudaDriverApi.cuInit(0));
            CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out var device, 0));
            CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out _context, 0, device));
            _isInitialized = true;
        }
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
        var cacheKey = $"{sourceHash}_{kernelName}";

        if (_functionCache.TryGetValue(cacheKey, out var cachedFunc))
        {
            return cachedFunc;
        }

        // Compilation path
        var tempDir = Path.Combine(Path.GetTempPath(), "Kassyi.GSharp.Runtime.JitCache");
        Directory.CreateDirectory(tempDir);

        var cuFile = Path.Combine(tempDir, $"{sourceHash}.cu");
        var fatbinFile = Path.Combine(tempDir, $"{sourceHash}.fatbin");

        if (!File.Exists(fatbinFile))
        {
            File.WriteAllText(cuFile, cppSource, new UTF8Encoding(false));

            // Invoke the existing compiler backend. 
            // In the future, dynamic constants (like SM arch) can be passed here.
            NvccCompiler.CompileToFatbin(cuFile, fatbinFile, null, tempDir);
        }

        var ptxBytes = File.ReadAllBytes(fatbinFile);

        CudaException.ThrowIfError(CudaDriverApi.cuModuleLoadDataEx(out var module, ptxBytes, 0, null, null));
        CudaException.ThrowIfError(CudaDriverApi.cuModuleGetFunction(out var hfunc, module, kernelName));

        _functionCache[cacheKey] = hfunc;

        return hfunc;
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
