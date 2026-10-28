
namespace Kassyi.GSharp.Compiler.Tasks.Toolchains;

/// <summary>
/// Windows / MSVC native toolchain implementation using <c>vswhere.exe</c>, <c>vcvarsall.bat</c>, and <c>cl.exe</c>.
/// </summary>
public sealed class MsvcToolchain : INativeToolchain
{
    private static readonly ConcurrentDictionary<string, Lazy<Dictionary<string, string>?>> s_vcvarsCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IProcessRunner _processRunner;
    private readonly bool _disableCache;
    private readonly string? _vswherePathOverride;
    private readonly Dictionary<string, Dictionary<string, string>> _resolvedEnvs =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="MsvcToolchain"/> class.
    /// </summary>
    /// <param name="processRunner">The process runner used to invoke external processes.</param>
    /// <param name="disableCache">If <see langword="true"/>, bypasses the static <c>vcvarsall.bat</c> environment cache.</param>
    /// <param name="vswherePathOverride">Optional explicit path to <c>vswhere.exe</c> (primarily for unit testing across OSes).</param>
    public MsvcToolchain(
        IProcessRunner processRunner,
        bool disableCache = false,
        string? vswherePathOverride = null)
    {
        _processRunner = processRunner;
        _disableCache = disableCache;
        _vswherePathOverride = vswherePathOverride;
    }

    /// <inheritdoc />
    public string Name => "MSVC";

    /// <inheritdoc />
    public bool IsWindows => true;

    /// <inheritdoc />
    public ToolchainPreparationResult PrepareEnvironment(
        string[] architectures,
        TaskLoggingHelper log,
        CancellationToken cancellationToken = default)
    {
        foreach (var arch in architectures)
        {
            var vcvarsArch = ToVcvarsArchitecture(arch);
            var env = GetVcvarsEnvironment(vcvarsArch, cancellationToken);
            if (env == null)
            {
                log.LogError(
                    $"Could not locate vcvarsall.bat via vswhere.exe for architecture {vcvarsArch}. Please ensure MSVC C++ Build Tools are installed.");
                return ToolchainPreparationResult.Failed;
            }

            _resolvedEnvs[vcvarsArch] = env;
        }

        return ToolchainPreparationResult.Ready;
    }

    /// <inheritdoc />
    public Dictionary<string, string>? GetEnvironmentVariables(string architecture)
    {
        var vcvarsArch = ToVcvarsArchitecture(architecture);
        if (_resolvedEnvs.TryGetValue(vcvarsArch, out var env))
        {
            return env;
        }

        return GetVcvarsEnvironment(vcvarsArch);
    }

    /// <summary>
    /// Resolves or retrieves the cached <c>vcvarsall.bat</c> environment variables for the specified architecture.
    /// </summary>
    /// <param name="vcvarsArch">The <c>vcvarsall.bat</c> architecture specification (e.g., <c>x64</c>, <c>x64_arm64</c>).</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A dictionary of environment variables, or <see langword="null"/> if resolution failed.</returns>
    public Dictionary<string, string>? GetVcvarsEnvironment(
        string vcvarsArch,
        CancellationToken cancellationToken = default)
    {
        if (_disableCache)
        {
            return ResolveVcvarsEnvironment(vcvarsArch, cancellationToken);
        }

        return s_vcvarsCache.GetOrAdd(
            vcvarsArch,
            arch => new Lazy<Dictionary<string, string>?>(() => ResolveVcvarsEnvironment(arch, cancellationToken))).Value;
    }

    /// <inheritdoc />
    public bool CompileCppToSharedLibrary(
        string sourcePath,
        string outputPath,
        string architecture,
        TaskLoggingHelper log,
        CancellationToken cancellationToken = default)
    {
        var vcvarsArch = ToVcvarsArchitecture(architecture);

        var baseDir = Path.GetDirectoryName(outputPath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(outputPath);
        var objPath = Path.Combine(baseDir, baseName + ".obj");

        // /O2 = Optimize for speed
        // /LD = Create DLL
        // /Fo = Object file name
        // /Fe = Output file name
        // /nologo = Suppress copyright message
        // /Zc:preprocessor = Fix CUDA 13.3+ fatal error C1189 with MSVC
        // /std:c++17 = Fix CUDA 13.3+ libcu++ requirement
        // /openmp = Enable OpenMP for BSP CPU scheduler parallelism
        // /EHs = C++ exceptions, also through extern "C" functions: a kernel step that cannot go on (a shared memory
        //        placement that fails) throws to the launcher, which catches it inside the OpenMP loop. /EHsc would let the
        //        compiler assume that the extern "C" kernels never throw
        // /bigobj = Allow more than 65,279 sections: one translation unit holds every kernel of the assembly, and a
        //           large one (the test assemblies) fails with C1128 without it
        var clArgs = $"/O2 /LD /nologo /std:c++17 /openmp /EHs /Zc:preprocessor /bigobj \"{sourcePath}\" /Fo:\"{objPath}\" /Fe:\"{outputPath}\"";

        log.LogMessage(MessageImportance.Normal, $"Compiling C++ source for {vcvarsArch}: {sourcePath}");

        var clExePath = "cl.exe";
        if (_resolvedEnvs.TryGetValue(vcvarsArch, out var env) ||
            (env = GetVcvarsEnvironment(vcvarsArch, cancellationToken)) != null)
        {
            if (env.TryGetValue("PATH", out var pathEnv))
            {
                var span = pathEnv.AsSpan();
                while (span.Length > 0)
                {
                    var sepIdx = span.IndexOf(Path.PathSeparator);
                    var segment = sepIdx >= 0 ? span.Slice(0, sepIdx) : span;
                    var fullPath = Path.Combine(segment.ToString(), "cl.exe");
                    if (File.Exists(fullPath))
                    {
                        clExePath = fullPath;
                        break;
                    }

                    if (sepIdx < 0)
                    {
                        break;
                    }

                    span = span.Slice(sepIdx + 1);
                }
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = clExePath,
            Arguments = clArgs
        };

        if (env != null)
        {
            foreach (var kvp in env)
            {
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        try
        {
            var result = _processRunner.Run(startInfo, cancellationToken);

            if (result.ExitCode == 0 && File.Exists(outputPath))
            {
                // MSVC produces .obj, .lib, .exp. Clean them up to avoid clutter.
                var libPath = Path.Combine(baseDir, baseName + ".lib");
                var expPath = Path.Combine(baseDir, baseName + ".exp");

                var srcDir = Path.GetDirectoryName(sourcePath) ?? "";
                var srcObjPath = Path.Combine(srcDir, Path.GetFileNameWithoutExtension(sourcePath) + ".obj");
                var cwdObjPath = Path.Combine(Environment.CurrentDirectory,
                    Path.GetFileNameWithoutExtension(sourcePath) + ".obj");

                CompileKernelTaskBase.CleanupFiles(objPath, libPath, expPath, srcObjPath, cwdObjPath);

                return true;
            }

            log.LogError($"MSVC compilation failed with exit code {result.ExitCode}.");
            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                log.LogError(result.StandardOutput);
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                log.LogError(result.StandardError);
            }

            RenamedIdentifierLogger.LogRenamedIdentifiers(log, result.StandardOutput, result.StandardError);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError($"Failed to start cl.exe. Ensure MSVC is installed. Details: {ex.Message}");
            return false;
        }
    }

    private static string ToVcvarsArchitecture(string arch) => arch switch
    {
        "x64" => "x64",
        "arm64" => "x64_arm64",
        _ => arch
    };

    private Dictionary<string, string>? ResolveVcvarsEnvironment(
        string vcvarsArch,
        CancellationToken cancellationToken)
    {
        // The vswhere query below is repeated by tools/collect-env/collect-env.cs (Msvc); keep both in sync.
        string vswhere;
        if (!string.IsNullOrEmpty(_vswherePathOverride))
        {
            vswhere = _vswherePathOverride!;
        }
        else
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            vswhere = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (!File.Exists(vswhere))
            {
                return null;
            }
        }

        var vswhereStartInfo = new ProcessStartInfo
        {
            FileName = vswhere,
            Arguments =
                "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            EnvironmentVariables =
            {
                ["VSCMD_SKIP_SENDTELEMETRY"] = "1"
            }
        };

        var vswhereResult = _processRunner.Run(vswhereStartInfo, cancellationToken);
        if (vswhereResult.ExitCode != 0)
        {
            return null;
        }

        var output = vswhereResult.StandardOutput.AsSpan().Trim().ToString();
        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        var vcvarsPath = Path.Combine(output, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        if (!File.Exists(vcvarsPath))
        {
            return null;
        }

        var envStartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"set VSCMD_SKIP_SENDTELEMETRY=1 && call \"{vcvarsPath}\" {vcvarsArch} >nul && set\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            EnvironmentVariables =
            {
                ["VSCMD_SKIP_SENDTELEMETRY"] = "1"
            }
        };

        var envResult = _processRunner.Run(envStartInfo, cancellationToken);
        if (envResult.ExitCode != 0)
        {
            return null;
        }

        var envOutput = envResult.StandardOutput;
        var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(envOutput);
        while (reader.ReadLine() is { } line)
        {
            var eqIndex = line.IndexOf('=');
            if (eqIndex > 0)
            {
                envVars[line[..eqIndex]] = line[(eqIndex + 1)..];
            }
        }

        return envVars;
    }
}
