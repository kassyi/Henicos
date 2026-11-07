
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
        CancellationToken cancellationToken = default,
        NativeSanitizers sanitizers = NativeSanitizers.None)
    {
        var vcvarsArch = ToVcvarsArchitecture(architecture);

        var paths = ClBuildPaths.For(outputPath);
        var clArgs = BuildClArguments(sourcePath, paths.ObjPath, outputPath, paths.VcPdbPath, sanitizers);

        log.LogMessage(MessageImportance.Normal, $"Compiling C++ source for {vcvarsArch}: {sourcePath}");

        var startInfo = CreateClStartInfo(clArgs, ResolveCompileEnvironment(vcvarsArch, cancellationToken));

        try
        {
            var result = _processRunner.Run(startInfo, cancellationToken);

            if (result.ExitCode == 0 && File.Exists(outputPath))
            {
                NvccOutputParser.LogCompilerWarnings(log, sourcePath, result.StandardOutput, result.StandardError);
                CleanupBuildArtifacts(log, sourcePath, paths.BaseDir, paths.BaseName, paths.ObjPath, paths.VcPdbPath);
                return true;
            }

            LogCompileFailure(log, result);
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

    // The intermediate files that cl.exe writes next to the output library.
    private readonly record struct ClBuildPaths(string BaseDir, string BaseName, string ObjPath, string VcPdbPath)
    {
        public static ClBuildPaths For(string outputPath)
        {
            var baseDir = Path.GetDirectoryName(outputPath) ?? "";
            var baseName = Path.GetFileNameWithoutExtension(outputPath);
            return new ClBuildPaths(
                baseDir,
                baseName,
                Path.Combine(baseDir, baseName + ".obj"),
                Path.Combine(baseDir, baseName + ".vc.pdb"));
        }
    }

    // The resolved vcvarsall environment of the architecture, or null when it cannot be resolved.
    private Dictionary<string, string>? ResolveCompileEnvironment(
        string vcvarsArch,
        CancellationToken cancellationToken)
    {
        if (_resolvedEnvs.TryGetValue(vcvarsArch, out var resolved) ||
            (resolved = GetVcvarsEnvironment(vcvarsArch, cancellationToken)) != null)
        {
            return resolved;
        }

        return null;
    }

    private static ProcessStartInfo CreateClStartInfo(string clArgs, Dictionary<string, string>? env)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FindClExe(env),
            Arguments = clArgs
        };

        if (env != null)
        {
            foreach (var kvp in env)
            {
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        return startInfo;
    }

    // cl.exe from the PATH of the vcvarsall environment, else the bare name.
    private static string FindClExe(Dictionary<string, string>? env)
    {
        if (env == null || !env.TryGetValue("PATH", out var pathEnv))
        {
            return "cl.exe";
        }

        var span = pathEnv.AsSpan();
        while (span.Length > 0)
        {
            var sepIdx = span.IndexOf(Path.PathSeparator);
            var segment = sepIdx >= 0 ? span.Slice(0, sepIdx) : span;
            var fullPath = Path.Combine(segment.ToString(), "cl.exe");
            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            if (sepIdx < 0)
            {
                break;
            }

            span = span.Slice(sepIdx + 1);
        }

        return "cl.exe";
    }

    // MSVC produces .obj, .lib, .exp. Clean them up to avoid clutter.
    private static void CleanupBuildArtifacts(
        TaskLoggingHelper log,
        string sourcePath,
        string baseDir,
        string baseName,
        string objPath,
        string vcPdbPath)
    {
        var libPath = Path.Combine(baseDir, baseName + ".lib");
        var expPath = Path.Combine(baseDir, baseName + ".exp");

        var srcDir = Path.GetDirectoryName(sourcePath) ?? "";
        var srcObjPath = Path.Combine(srcDir, Path.GetFileNameWithoutExtension(sourcePath) + ".obj");
        var cwdObjPath = Path.Combine(Environment.CurrentDirectory,
            Path.GetFileNameWithoutExtension(sourcePath) + ".obj");

        var ilkPath = Path.Combine(baseDir, baseName + ".ilk");
        CompileKernelTaskBase.CleanupFiles(log, objPath, libPath, expPath, ilkPath, vcPdbPath, srcObjPath, cwdObjPath);
    }

    private static void LogCompileFailure(TaskLoggingHelper log, ProcessResult result)
    {
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
    }

    // Logged once per task run by CompileCppToNativeTask when 'undefined' is requested with this toolchain.
    internal const string UndefinedSanitizerUnsupportedWarning =
        "GSharpNativeSanitizers specifies 'undefined', which is not supported by MSVC (no UndefinedBehaviorSanitizer). Continuing with AddressSanitizer only when 'address' is specified, ignoring 'undefined'.";

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
    // /fsanitize=address /Zi /Fd = AddressSanitizer with debug information for symbolized reports ('undefined' has no
    //                              MSVC counterpart and is ignored)
    private static string BuildClArguments(
        string sourcePath,
        string objPath,
        string outputPath,
        string vcPdbPath,
        NativeSanitizers sanitizers)
    {
        var sanitizerFlags = (sanitizers & NativeSanitizers.Address) != 0
            ? $" /fsanitize=address /Zi /Fd:\"{vcPdbPath}\""
            : "";

        return $"/O2 /LD /nologo /std:c++17 /openmp /EHs /Zc:preprocessor /bigobj{sanitizerFlags} \"{sourcePath}\" /Fo:\"{objPath}\" /Fe:\"{outputPath}\"";
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
        var vswhere = LocateVswhere();
        if (vswhere == null)
        {
            return null;
        }

        var installationPath = QueryInstallationPath(vswhere, cancellationToken);
        if (string.IsNullOrEmpty(installationPath))
        {
            return null;
        }

        var vcvarsPath = Path.Combine(installationPath, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        if (!File.Exists(vcvarsPath))
        {
            return null;
        }

        return RunVcvars(vcvarsPath, vcvarsArch, cancellationToken);
    }

    private string? LocateVswhere()
    {
        if (_vswherePathOverride is { Length: > 0 })
        {
            return _vswherePathOverride;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var vswhere = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        return File.Exists(vswhere) ? vswhere : null;
    }

    // The vswhere query below is repeated by tools/collect-env/collect-env.cs (Msvc); keep both in sync.
    private string? QueryInstallationPath(string vswhere, CancellationToken cancellationToken)
    {
        var vswhereStartInfo = new ProcessStartInfo
        {
            FileName = vswhere,
            Arguments = "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 " +
                "-property installationPath",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            EnvironmentVariables =
            {
                ["VSCMD_SKIP_SENDTELEMETRY"] = "1"
            }
        };

        var vswhereResult = _processRunner.Run(vswhereStartInfo, cancellationToken);
        return vswhereResult.ExitCode == 0 ? vswhereResult.StandardOutput.AsSpan().Trim().ToString() : null;
    }

    // Runs vcvarsall.bat in cmd.exe and reads the environment it leaves (the output of `set`).
    private Dictionary<string, string>? RunVcvars(
        string vcvarsPath,
        string vcvarsArch,
        CancellationToken cancellationToken)
    {
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
        return envResult.ExitCode == 0 ? ParseEnvironment(envResult.StandardOutput) : null;
    }

    private static Dictionary<string, string> ParseEnvironment(string setOutput)
    {
        var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(setOutput);
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
