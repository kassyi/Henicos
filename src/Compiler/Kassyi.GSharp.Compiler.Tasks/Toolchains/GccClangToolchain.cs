namespace Kassyi.GSharp.Compiler.Tasks.Toolchains;

/// <summary>
/// POSIX / Linux native toolchain implementation using <c>g++</c> or <c>clang++</c> to build shared libraries.
/// </summary>
public sealed class GccClangToolchain : INativeToolchain
{
    private static readonly string[] s_defaultCandidates = ["g++", "clang++", "c++"];

    private readonly IProcessRunner _processRunner;
    private readonly string[] _compilerCandidates;
    private string? _resolvedCompiler;

    /// <summary>
    /// Initializes a new instance of the <see cref="GccClangToolchain"/> class.
    /// </summary>
    /// <param name="processRunner">The process runner used to invoke external processes.</param>
    /// <param name="compilerExecutable">Optional explicit compiler executable name or path (defaults to probing <c>CXX</c>, <c>g++</c>, <c>clang++</c>).</param>
    public GccClangToolchain(IProcessRunner processRunner, string? compilerExecutable = null)
    {
        _processRunner = processRunner;
        if (!string.IsNullOrWhiteSpace(compilerExecutable))
        {
            _compilerCandidates = [compilerExecutable!];
        }
        else
        {
            var cxxEnv = Environment.GetEnvironmentVariable("CXX");
            _compilerCandidates = !string.IsNullOrWhiteSpace(cxxEnv)
                ? [cxxEnv!, .. s_defaultCandidates]
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
        CancellationToken cancellationToken = default)
    {
        var compiler = _resolvedCompiler ?? _compilerCandidates[0];

        // -O2 = Optimize for speed
        // -shared -fPIC = Build position-independent shared library
        // -std=c++17 = C++17 standard
        // -fopenmp = Enable OpenMP for BSP CPU scheduler parallelism
        var args = $"-O2 -shared -fPIC -std=c++17 -fopenmp \"{sourcePath}\" -o \"{outputPath}\"";

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
