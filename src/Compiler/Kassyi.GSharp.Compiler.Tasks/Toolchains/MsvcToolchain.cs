
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
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _resolvedEnvs =
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
    public CompilerIdentity GetCompilerIdentity(
        string architecture,
        CancellationToken cancellationToken = default)
    {
        var vcvarsArch = ToVcvarsArchitecture(architecture);
        var env = ResolveCompileEnvironment(vcvarsArch, cancellationToken);
        var clPath = NativeToolchainHelper.FindExecutableInPath("cl.exe", env);
        return new CompilerIdentity(clPath, ReadMsvcVersion(clPath, env));
    }

    /// <inheritdoc />
    public string BuildCompileObjectArguments(
        string sourcePath,
        string objPath,
        string architecture,
        NativeSanitizers sanitizers = NativeSanitizers.None,
        string? pdbPath = null)
    {
        var sanitizerFlags = string.Empty;
        if ((sanitizers & NativeSanitizers.Address) != 0)
        {
            var effectivePdb = !string.IsNullOrEmpty(pdbPath)
                ? pdbPath
                : Path.ChangeExtension(objPath, ".vc.pdb");
            sanitizerFlags = $" /fsanitize=address /Zi /Fd:\"{effectivePdb}\"";
        }

        return $"{OptimizationFlags} {CompileFlags}{sanitizerFlags} \"{sourcePath}\" /Fo:\"{objPath}\"";
    }

    /// <inheritdoc />
    public bool TryCompileToObject(
        string sourcePath,
        string objPath,
        string architecture,
        TaskLoggingHelper log,
        CancellationToken cancellationToken = default,
        NativeSanitizers sanitizers = NativeSanitizers.None,
        string? pdbPath = null)
    {
        var vcvarsArch = ToVcvarsArchitecture(architecture);
        NativeToolchainHelper.EnsureParentDirectory(objPath);
        var clArgs = BuildCompileObjectArguments(sourcePath, objPath, architecture, sanitizers, pdbPath);

        log.LogMessage(MessageImportance.Normal, $"Compiling C++ source for {vcvarsArch}: {sourcePath}");
        return TryRunCl(
            clArgs,
            vcvarsArch,
            sourcePath,
            objPath,
            log,
            cancellationToken);
    }

    /// <inheritdoc />
    public bool TryLinkSharedLibrary(
        IReadOnlyList<string> objPaths,
        string outputPath,
        string architecture,
        TaskLoggingHelper log,
        CancellationToken cancellationToken = default,
        NativeSanitizers sanitizers = NativeSanitizers.None)
    {
        if (objPaths.Count == 0)
        {
            log.LogError("Cannot link native shared library with zero object files.");
            return false;
        }

        var vcvarsArch = ToVcvarsArchitecture(architecture);
        NativeToolchainHelper.EnsureParentDirectory(outputPath);

        var joinedObjs = NativeToolchainHelper.JoinQuotedPaths(objPaths);
        var sanitizerFlags = (sanitizers & NativeSanitizers.Address) != 0 ? " /fsanitize=address /Zi" : "";
        var linkArgs = $"{LinkFlags}{sanitizerFlags} {joinedObjs} /Fe:\"{outputPath}\"";

        log.LogMessage(
            MessageImportance.Normal,
            $"Linking {objPaths.Count} C++ object(s) for {vcvarsArch}: {outputPath}");

        if (!TryRunCl(linkArgs, vcvarsArch, outputPath, outputPath, log, cancellationToken))
        {
            return false;
        }

        var baseDir = Path.GetDirectoryName(outputPath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(outputPath);
        CleanupLinkArtifacts(log, baseDir, baseName);
        return true;
    }

    /// <inheritdoc />
    public bool TryCompileCppToSharedLibrary(
        string sourcePath,
        string outputPath,
        string architecture,
        TaskLoggingHelper log,
        CancellationToken cancellationToken = default,
        NativeSanitizers sanitizers = NativeSanitizers.None) =>
        NativeToolchainHelper.TryCompileSingleSourceToSharedLibrary(
            this, sourcePath, outputPath, architecture, log, cancellationToken, sanitizers);

    private bool TryRunCl(
        string clArgs,
        string vcvarsArch,
        string warningContextFile,
        string expectedOutputPath,
        TaskLoggingHelper log,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateClStartInfo(clArgs, ResolveCompileEnvironment(vcvarsArch, cancellationToken));

        try
        {
            var result = _processRunner.Run(startInfo, cancellationToken);

            if (result.ExitCode == 0 && NativeToolchainHelper.HasNonEmptyFile(expectedOutputPath))
            {
                NvccOutputParser.LogCompilerWarnings(
                    log, warningContextFile, result.StandardOutput, result.StandardError);
                return true;
            }

            if (result.ExitCode == 0)
            {
                log.LogError(
                    $"MSVC exited with code 0 without producing a non-empty output '{expectedOutputPath}'.");
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

    // The resolved vcvarsall environment of the architecture, or null when it cannot be resolved.
    private Dictionary<string, string>? ResolveCompileEnvironment(
        string vcvarsArch,
        CancellationToken cancellationToken)
    {
        if (_resolvedEnvs.TryGetValue(vcvarsArch, out var resolved))
        {
            return resolved;
        }

        resolved = GetVcvarsEnvironment(vcvarsArch, cancellationToken);
        if (resolved is not null)
        {
            _resolvedEnvs[vcvarsArch] = resolved;
        }

        return resolved;
    }

    private static string ReadMsvcVersion(string clPath, Dictionary<string, string>? env)
    {
        if (File.Exists(clPath))
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(clPath);
                if (!string.IsNullOrWhiteSpace(info.ProductVersion))
                {
                    return info.ProductVersion.AsSpan().Trim().ToString();
                }

                if (!string.IsNullOrWhiteSpace(info.FileVersion))
                {
                    return info.FileVersion.AsSpan().Trim().ToString();
                }
            }
            // smell-scan: ignore swallowed-exception -- 0-byte fake cl.exe in tests; fall back to VCToolsVersion
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Fall back to VCToolsVersion below.
            }
        }

        if (env is not null &&
            env.TryGetValue("VCToolsVersion", out var vcTools) &&
            !string.IsNullOrWhiteSpace(vcTools))
        {
            return vcTools.AsSpan().Trim().ToString();
        }

        return "unknown";
    }

    private static ProcessStartInfo CreateClStartInfo(string clArgs, Dictionary<string, string>? env)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = NativeToolchainHelper.FindExecutableInPath("cl.exe", env),
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

    // MSVC link produces .lib, .exp, and .ilk alongside the DLL; clean them without removing DLL or optional .pdb.
    private static void CleanupLinkArtifacts(
        TaskLoggingHelper log,
        string baseDir,
        string baseName)
    {
        var libPath = Path.Combine(baseDir, baseName + ".lib");
        var expPath = Path.Combine(baseDir, baseName + ".exp");
        var ilkPath = Path.Combine(baseDir, baseName + ".ilk");
        CompileKernelTaskBase.CleanupFiles(log, libPath, expPath, ilkPath);
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

    // The optimization flags of the native kernel build: /O2 without its global optimizations (/Og), that is
    // /Oi /Ot /Oy /Ob2 /GF /Gy (#1078). MSVC's /Og miscompiles C++ that the emitted kernels contain and that C++
    // defines:
    // - a wrapping negation used as a dividend, (int)(0u - (unsigned)x) / 2, becomes -(x / 2), wrong for int.MinValue
    //   (Developer Community 11177375; still wrong in 19.53.36919)
    // - a short widened through unsigned int and widened directly to unsigned long long reuses the wrong sign
    //   extension (Developer Community 11127140; fixed in 19.53.36919)
    // - the conversion back to short after a wrapping add is dropped when the value then picks a pointer target (fixed
    //   in 19.52.37025)
    // Everything else of /O2 stays, the inlining of the Prelude helpers (/Ob2) included. /Og- is not used because
    // MSVC deprecates it (D9035), and /Gs is not added because it is not part of /O2 (it puts a stack probe in every
    // function with locals). This is the one place to go back to /O2 once a compiler with all three fixes is
    // detected.
    private const string OptimizationFlags = "/Oi /Ot /Oy /Ob2 /GF /Gy";

    // Compile/link flags besides optimization and sanitizers (BuildCompileObjectArguments & TryLinkSharedLibrary).
    private const string CompileFlags = "/c /nologo /std:c++17 /openmp /EHs /Zc:preprocessor /bigobj /we4533";
    private const string LinkFlags = "/nologo /LD /openmp";

    // Logged once per task run by CompileCppToNativeTask when 'undefined' is requested with this toolchain.
    internal const string UndefinedSanitizerUnsupportedWarning =
        "GSharpNativeSanitizers specifies 'undefined', which is not supported by MSVC (no UndefinedBehaviorSanitizer). Continuing with AddressSanitizer only when 'address' is specified, ignoring 'undefined'.";

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

    // The vswhere query below is repeated by tools/collect-env/ToolchainProbe.cs (Msvc); keep both in sync.
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
