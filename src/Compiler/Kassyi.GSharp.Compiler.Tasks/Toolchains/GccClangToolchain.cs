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
    private string? _resolvedCompiler;

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
            if (IsCompilerAvailable(candidate, cancellationToken))
            {
                _resolvedCompiler = candidate;
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
    public bool CompileCppToSharedLibrary(
        string sourcePath,
        string outputPath,
        string architecture,
        TaskLoggingHelper log,
        CancellationToken cancellationToken = default,
        NativeSanitizers sanitizers = NativeSanitizers.None)
    {
        var compiler = _resolvedCompiler ?? _compilerCandidates[0];

        // -O2 = Optimize for speed
        // -shared -fPIC = Build position-independent shared library
        // -std=c++17 = C++17 standard
        // -fopenmp = Enable OpenMP for BSP CPU scheduler parallelism
        // -Wno-unused-label = C# already diagnoses unused user labels (CS0164); synthesized labels may be unreferenced
        var args = $"-O2 -shared -fPIC -std=c++17 -fopenmp -Wno-unused-label{BuildSanitizerFlags(sanitizers)} \"{sourcePath}\" -o \"{outputPath}\"";

        log.LogMessage(MessageImportance.Normal, $"Compiling C++ source for {architecture} with {compiler}: {sourcePath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = compiler,
            Arguments = args
        };

        try
        {
            var result = _processRunner.Run(startInfo, cancellationToken);

            if (result.ExitCode == 0 && File.Exists(outputPath))
            {
                NvccOutputParser.LogCompilerWarnings(log, sourcePath, result.StandardOutput, result.StandardError);
                return true;
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

    // " -fsanitize=<names> [-fno-sanitize-recover=undefined] -fno-omit-frame-pointer -g", or "" when none is enabled.
    // -fno-sanitize-recover=undefined stops at the first UBSan report; -fno-omit-frame-pointer and -g give readable stacks.
    private static string BuildSanitizerFlags(NativeSanitizers sanitizers)
    {
        if (sanitizers == NativeSanitizers.None)
        {
            return "";
        }

        var names = new List<string>(2);
        if ((sanitizers & NativeSanitizers.Address) != 0)
        {
            names.Add("address");
        }

        var enableUndefined = (sanitizers & NativeSanitizers.Undefined) != 0;
        if (enableUndefined)
        {
            names.Add("undefined");
        }

        var recover = enableUndefined ? " -fno-sanitize-recover=undefined" : "";
        return $" -fsanitize={string.Join(",", names)}{recover} -fno-omit-frame-pointer -g";
    }

    private bool IsCompilerAvailable(string executable, CancellationToken cancellationToken)
    {
        try
        {
            var checkInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--version"
            };
            var result = _processRunner.Run(checkInfo, cancellationToken);
            return result.ExitCode == 0;
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
}
