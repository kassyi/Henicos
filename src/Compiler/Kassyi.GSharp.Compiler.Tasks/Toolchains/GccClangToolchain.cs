namespace Kassyi.GSharp.Compiler.Tasks.Toolchains;

/// <summary>
/// POSIX / Linux native toolchain implementation using <c>g++</c> or <c>clang++</c> to build shared libraries.
/// </summary>
public sealed class GccClangToolchain : INativeToolchain
{
    private static readonly string[] s_defaultCandidates = ["g++", "clang++", "c++"];

    private readonly IProcessRunner _processRunner;
    private readonly string[] _compilerCandidates;
    private readonly string _hostArchitecture;
    private readonly object _compilerResolutionLock = new();
    private string? _resolvedCompiler;
    private string? _resolvedVersion;

    /// <summary>
    /// Initializes a new instance of the <see cref="GccClangToolchain"/> class.
    /// </summary>
    /// <param name="processRunner">The process runner used to invoke external processes.</param>
    /// <param name="compilerExecutable">Optional explicit compiler executable name or path (defaults to probing <c>CXX</c>, <c>g++</c>, <c>clang++</c>).</param>
    /// <param name="hostArchitecture">
    /// The architecture (<c>x64</c> or <c>arm64</c>) the compiler builds for; defaults to the architecture of the build
    /// process.
    /// </param>
    public GccClangToolchain(IProcessRunner processRunner, string? compilerExecutable = null,
        string? hostArchitecture = null)
    {
        _processRunner = processRunner;
        _hostArchitecture = hostArchitecture ?? CompileCppToNativeTask.HostArchitecture;
        if (compilerExecutable is { Length: > 0 } && !string.IsNullOrWhiteSpace(compilerExecutable))
        {
            _compilerCandidates = [compilerExecutable];
        }
        else
        {
            var cxxEnv = Environment.GetEnvironmentVariable("CXX");
            _compilerCandidates = cxxEnv is { Length: > 0 } && !string.IsNullOrWhiteSpace(cxxEnv)
                ? [cxxEnv, .. s_defaultCandidates]
                : s_defaultCandidates;
        }
    }

    /// <inheritdoc />
    public string Name => "GCC/Clang";

    /// <inheritdoc />
    public bool IsWindows => false;

    /// <inheritdoc />
    public ToolchainPreparationResult PrepareEnvironment(
        string[] architectures,
        TaskLoggingHelper log,
        CancellationToken cancellationToken = default)
    {
        // The compiler is invoked without a target option, so it builds for the host only. A library for another
        // architecture would be a host image under that architecture's name, which fails only when it is loaded.
        foreach (var architecture in architectures)
        {
            if (!string.Equals(architecture, _hostArchitecture, StringComparison.Ordinal))
            {
                log.LogError(
                    $"The GCC/Clang toolchain cannot build native CPU kernels for {architecture}: it builds only for " +
                    $"the host architecture ({_hostArchitecture}) and does not cross-compile. " +
                    $"Remove GSharpMultiTarget, or build on an {architecture} machine.");
                return ToolchainPreparationResult.Failed;
            }
        }

        foreach (var candidate in _compilerCandidates)
        {
            if (TryProbeCompiler(candidate, cancellationToken, out var version))
            {
                _resolvedCompiler = candidate;
                _resolvedVersion = version;
                return ToolchainPreparationResult.Ready;
            }
        }

        var probed = string.Join(", ", _compilerCandidates);
        log.LogWarning(
            $"C++ compiler was not found (probed: {probed}). Skipping native CPU kernel compilation; execution will fall back to the managed C# runner. Install g++ or clang++ (with OpenMP support) to enable native CPU AOT compilation.");
        return ToolchainPreparationResult.SkipCompilation;
    }

    /// <inheritdoc />
    public Dictionary<string, string>? GetEnvironmentVariables(string architecture) => null;

    /// <inheritdoc />
    public CompilerIdentity GetCompilerIdentity(
        string architecture,
        CancellationToken cancellationToken = default)
    {
        var (compiler, version) = ResolveCompilerAndVersion(cancellationToken);
        var resolvedPath = NativeToolchainHelper.FindExecutableInPath(compiler, env: null);
        // Read by the compiler from the environment the build runs in: the C++ include paths, the options Clang adds
        // to its command line, and the folders GCC takes its compiler passes and assembler from
        return new CompilerIdentity(resolvedPath, version,
            NativeToolchainHelper.DescribeEnvironment(null,
                "CPATH", "CPLUS_INCLUDE_PATH", "CCC_OVERRIDE_OPTIONS", "GCC_EXEC_PREFIX", "COMPILER_PATH"));
    }

    // [WHY] -fno-strict-aliasing: C# reads one memory through views of different element types (MemoryMarshal.Cast,
    // an [In] Memory<short> and ArraySegment<int> of one array), and the generated code reaches it through pointers of
    // those types; -O2 would let gcc and clang assume they never overlap and read a value from before a write. MSVC
    // never makes that assumption
    /// <inheritdoc />
    public string BuildCompileObjectArguments(
        string sourcePath,
        string objPath,
        string architecture,
        NativeSanitizers sanitizers = NativeSanitizers.None,
        string? pdbPath = null,
        bool runsOnNativeStacks = false)
    {
        var stackClashFlag = runsOnNativeStacks ? " -fstack-clash-protection" : "";
        return "-c -O2 -fno-strict-aliasing -fPIC -std=c++17 -fopenmp -Wno-unused-label" +
               $"{stackClashFlag}{BuildSanitizerFlags(sanitizers)} " +
               $"\"{sourcePath}\" -o \"{objPath}\"";
    }

    /// <inheritdoc />
    public bool TryCompileToObject(
        string sourcePath,
        string objPath,
        string architecture,
        TaskLoggingHelper log,
        CancellationToken cancellationToken = default,
        NativeSanitizers sanitizers = NativeSanitizers.None,
        string? pdbPath = null,
        bool runsOnNativeStacks = false)
    {
        var (compiler, _) = ResolveCompilerAndVersion(cancellationToken);
        NativeToolchainHelper.EnsureParentDirectory(objPath);
        var args = BuildCompileObjectArguments(
            sourcePath, objPath, architecture, sanitizers, pdbPath, runsOnNativeStacks);

        log.LogMessage(
            MessageImportance.Normal,
            $"Compiling C++ source for {architecture} with {compiler}: {sourcePath}");

        return TryRunCompiler(compiler, args, sourcePath, objPath, log, cancellationToken);
    }

    /// <inheritdoc />
    public bool TryLinkSharedLibrary(
        IReadOnlyList<string> objPaths,
        string outputPath,
        string architecture,
        TaskLoggingHelper log,
        CancellationToken cancellationToken = default,
        NativeSanitizers sanitizers = NativeSanitizers.None,
        bool runsOnNativeStacks = false)
    {
        if (objPaths.Count == 0)
        {
            log.LogError("Cannot link native shared library with zero object files.");
            return false;
        }

        var (compiler, _) = ResolveCompilerAndVersion(cancellationToken);
        NativeToolchainHelper.EnsureParentDirectory(outputPath);

        var joinedObjs = NativeToolchainHelper.JoinQuotedPaths(objPaths);
        // Keep the shared library mapped after dlopen/NativeLibrary.Free so an installed SIGSEGV handler is never unmapped.
        var nodeleteFlag = runsOnNativeStacks ? " -Wl,-z,nodelete" : "";
        var args = $"-shared -fopenmp{nodeleteFlag}{BuildSanitizerFlags(sanitizers)} {joinedObjs} -o \"{outputPath}\"";

        log.LogMessage(
            MessageImportance.Normal,
            $"Linking {objPaths.Count} C++ object(s) for {architecture} with {compiler}: {outputPath}");

        return TryRunCompiler(compiler, args, outputPath, outputPath, log, cancellationToken);
    }

    /// <inheritdoc />
    public bool TryCompileCppToSharedLibrary(
        string sourcePath,
        string outputPath,
        string architecture,
        TaskLoggingHelper log,
        CancellationToken cancellationToken = default,
        NativeSanitizers sanitizers = NativeSanitizers.None,
        bool runsOnNativeStacks = false) =>
        NativeToolchainHelper.TryCompileSingleSourceToSharedLibrary(
            this, sourcePath, outputPath, architecture, log, cancellationToken, sanitizers, runsOnNativeStacks);

    private bool TryRunCompiler(
        string compiler,
        string args,
        string warningContextFile,
        string expectedOutputPath,
        TaskLoggingHelper log,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = compiler,
            Arguments = args
        };

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
                    $"{compiler} exited with code 0 without producing a non-empty output '{expectedOutputPath}'.");
            }

            log.LogError($"{compiler} compilation failed with exit code {result.ExitCode}.");
            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                log.LogMessage(MessageImportance.High, result.StandardOutput);
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
            log.LogError($"Failed to start {compiler}. Ensure a C++ compiler is installed. Details: {ex.Message}");
            return false;
        }
    }

    // " -fsanitize=<names> [-fno-sanitize-recover=undefined] -fno-omit-frame-pointer -g", or "" when disabled.
    // -fno-sanitize-recover=undefined stops at the first UBSan report; -fno-omit-frame-pointer and -g give readable stacks.
    private static string BuildSanitizerFlags(NativeSanitizers sanitizers)
    {
        var hasAddress = (sanitizers & NativeSanitizers.Address) != 0;
        var hasUndefined = (sanitizers & NativeSanitizers.Undefined) != 0;
        if (!hasAddress && !hasUndefined)
        {
            return "";
        }

        var names = (hasAddress, hasUndefined) switch
        {
            (true, true) => "address,undefined",
            (true, false) => "address",
            (false, true) => "undefined",
            _ => ""
        };
        var recover = hasUndefined ? " -fno-sanitize-recover=undefined" : "";
        return $" -fsanitize={names}{recover} -fno-omit-frame-pointer -g";
    }

    private (string Compiler, string Version) ResolveCompilerAndVersion(CancellationToken cancellationToken)
    {
        lock (_compilerResolutionLock)
        {
            if (_resolvedCompiler is not null)
            {
                return (_resolvedCompiler, _resolvedVersion ?? "unknown");
            }

            foreach (var candidate in _compilerCandidates)
            {
                if (TryProbeCompiler(candidate, cancellationToken, out var version))
                {
                    _resolvedCompiler = candidate;
                    _resolvedVersion = version;
                    return (candidate, version);
                }
            }

            return (_compilerCandidates[0], "unknown");
        }
    }

    private bool TryProbeCompiler(string executable, CancellationToken cancellationToken, out string version)
    {
        version = "unknown";
        try
        {
            var checkInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--version"
            };
            var result = _processRunner.Run(checkInfo, cancellationToken);
            if (result.ExitCode != 0)
            {
                return false;
            }

            version = ExtractFirstNonEmptyLine(result.StandardOutput);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractFirstNonEmptyLine(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "unknown";
        }

        using var reader = new StringReader(output);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.AsSpan().Trim();
            if (trimmed.Length > 0)
            {
                return trimmed.ToString();
            }
        }

        return "unknown";
    }
}
