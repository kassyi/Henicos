
namespace Kassyi.GSharp.Compiler.Tasks.Toolchains;

/// <summary>
/// Windows / MSVC native toolchain implementation using <c>vswhere.exe</c>, <c>vcvarsall.bat</c>, and <c>cl.exe</c>.
/// </summary>
public sealed class MsvcToolchain : INativeToolchain
{
    private readonly IProcessRunner _processRunner;
    private readonly MsvcEnvironmentResolver _environment;

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
        _environment = new MsvcEnvironmentResolver(processRunner, disableCache, vswherePathOverride);
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
            var vcvarsArch = MsvcEnvironmentResolver.ToVcvarsArchitecture(arch);
            var env = GetVcvarsEnvironment(vcvarsArch, cancellationToken);
            if (env == null)
            {
                log.LogError(
                    $"Could not locate vcvarsall.bat via vswhere.exe for architecture {vcvarsArch}. Please ensure MSVC C++ Build Tools are installed.");
                return ToolchainPreparationResult.Failed;
            }

            _environment.Remember(vcvarsArch, env);
        }

        return ToolchainPreparationResult.Ready;
    }

    /// <inheritdoc />
    public Dictionary<string, string>? GetEnvironmentVariables(string architecture)
    {
        return _environment.GetEnvironment(MsvcEnvironmentResolver.ToVcvarsArchitecture(architecture));
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
        return _environment.GetVcvarsEnvironment(vcvarsArch, cancellationToken);
    }

    /// <inheritdoc />
    public CompilerIdentity GetCompilerIdentity(
        string architecture,
        CancellationToken cancellationToken = default)
    {
        var vcvarsArch = MsvcEnvironmentResolver.ToVcvarsArchitecture(architecture);
        var env = _environment.ResolveCompileEnvironment(vcvarsArch, cancellationToken);
        var clPath = NativeToolchainHelper.FindExecutableInPath("cl.exe", env);
        return new CompilerIdentity(clPath, MsvcEnvironmentResolver.ReadMsvcVersion(clPath, env));
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
        var vcvarsArch = MsvcEnvironmentResolver.ToVcvarsArchitecture(architecture);
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

        var vcvarsArch = MsvcEnvironmentResolver.ToVcvarsArchitecture(architecture);
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
        var startInfo = CreateClStartInfo(
            clArgs, _environment.ResolveCompileEnvironment(vcvarsArch, cancellationToken));

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
}
