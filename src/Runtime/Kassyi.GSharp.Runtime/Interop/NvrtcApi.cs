using System.Reflection;

using System.Diagnostics.CodeAnalysis;

namespace Kassyi.GSharp.Runtime.Interop;

[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Enum maps to native C++ API values")]
public enum NvrtcResult
{
    Success = 0,
    OutOfMemory = 1,
    ProgramCreationFailure = 2,
    InvalidInput = 3,
    InvalidProgram = 4,
    InvalidOption = 5,
    Compilation = 6,
    BuiltinOperationFailure = 7,
    NoNameExpressionsAfterCompilation = 8,
    NoLoweredNamesBeforeCompilation = 9,
    NameExpressionNotValid = 10,
    InternalError = 11
}

public static class NvrtcApi
{
    private const string DllName = "nvrtc_custom";

    static NvrtcApi()
    {
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        var addedToPath = false;

        if (!string.IsNullOrEmpty(cudaPath))
        {
            var binPath = Path.Combine(cudaPath, "bin");
            var binX64Path = Path.Combine(binPath, "x64");
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (Directory.Exists(binX64Path) && pathEnv != null && !pathEnv.Contains(binX64Path))
            {
                Environment.SetEnvironmentVariable("PATH", pathEnv + ";" + binX64Path + ";" + binPath);
                addedToPath = true;
            }
        }

        if (!addedToPath)
        {
            var cudaBaseDir = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
            if (Directory.Exists(cudaBaseDir))
            {
                foreach (var dir in Directory.GetDirectories(cudaBaseDir).OrderByDescending(d => d))
                {
                    var binPath = Path.Combine(dir, "bin");
                    var binX64Path = Path.Combine(binPath, "x64");
                    if (Directory.Exists(binX64Path))
                    {
                        var pathEnv = Environment.GetEnvironmentVariable("PATH");
                        Environment.SetEnvironmentVariable("PATH", pathEnv + ";" + binX64Path + ";" + binPath);
                        break;
                    }
                }
            }
        }

        NativeLibrary.SetDllImportResolver(typeof(NvrtcApi).Assembly, ImportResolver);
    }

    private static IntPtr ImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA") == "1")
        {
            throw new DllNotFoundException($"Simulated missing library: {libraryName} (GSHARP_TEST_NO_CUDA=1)");
        }

        if (libraryName != DllName)
        {
            return IntPtr.Zero;
        }

        string[] potentialNames =
        [
            "nvrtc64_130_0.dll",
            "nvrtc64_120_0.dll",
            "nvrtc64_112_0.dll",
            "nvrtc.dll"
        ];

        foreach (var name in potentialNames)
        {
            if (NativeLibrary.TryLoad(name, assembly, searchPath, out var handle))
            {
                return handle;
            }
        }

        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrEmpty(cudaPath))
        {
            var binPath = Path.Combine(cudaPath, "bin");
            var binX64Path = Path.Combine(binPath, "x64");

            string[] pathsToProbe = [binPath, binX64Path];

            foreach (var path in pathsToProbe)
            {
                if (Directory.Exists(path))
                {
                    foreach (var name in potentialNames)
                    {
                        var fullPath = Path.Combine(path, name);
                        if (NativeLibrary.TryLoad(fullPath, out var handle))
                        {
                            return handle;
                        }
                    }
                }
            }
        }

        var cudaBaseDir = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
        if (Directory.Exists(cudaBaseDir))
        {
            foreach (var dir in Directory.GetDirectories(cudaBaseDir).OrderByDescending(d => d))
            {
                var binPath = Path.Combine(dir, "bin");
                var binX64Path = Path.Combine(binPath, "x64");
                string[] pathsToProbe = [binPath, binX64Path];

                foreach (var path in pathsToProbe)
                {
                    if (Directory.Exists(path))
                    {
                        foreach (var name in potentialNames)
                        {
                            var fullPath = Path.Combine(path, name);
                            if (NativeLibrary.TryLoad(fullPath, out var handle))
                            {
                                return handle;
                            }
                        }
                    }
                }
            }
        }

        return IntPtr.Zero;
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern NvrtcResult nvrtcCreateProgram(
        out IntPtr prog,
        string src,
        string name,
        int numHeaders,
        string[]? headers,
        string[]? includeNames);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern NvrtcResult nvrtcDestroyProgram(ref IntPtr prog);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern NvrtcResult nvrtcCompileProgram(
        IntPtr prog,
        int numOptions,
        string[]? options);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern NvrtcResult nvrtcGetPTXSize(IntPtr prog, out nuint ptxSizeRet);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern NvrtcResult nvrtcGetPTX(IntPtr prog, byte[] ptx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern NvrtcResult nvrtcGetProgramLogSize(IntPtr prog, out nuint logSizeRet);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern NvrtcResult nvrtcGetProgramLog(IntPtr prog, byte[] log);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern NvrtcResult nvrtcVersion(out int major, out int minor);

    public static string GetNvrtcVersion()
    {
        try
        {
            NvrtcException.ThrowIfError(nvrtcVersion(out var major, out var minor));
            return $"{major}.{minor}";
        }
        catch
        {
            return "unknown";
        }
    }
}

public class NvrtcException(NvrtcResult result, string message)
    : Exception($"NVRTC Error ({result}): {message}")
{
    public NvrtcResult Result { get; } = result;

    public static void ThrowIfError(NvrtcResult result, string message = "")
    {
        if (result != NvrtcResult.Success)
        {
            throw new NvrtcException(result, string.IsNullOrEmpty(message) ? "NVRTC operation failed." : message);
        }
    }
}
