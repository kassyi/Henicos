namespace Kassyi.GSharp.Compiler.Tasks.Toolchains;

/// <summary>
/// Abstracts platform-specific native C++ toolchains (such as MSVC on Windows or GCC/Clang on Linux/POSIX)
/// used by G# MSBuild tasks to compile host C++ shadow kernels and configure CUDA host compiler environments.
/// </summary>
public interface INativeToolchain
{
    /// <summary>Gets the display name of the toolchain (e.g., "MSVC", "GCC/Clang").</summary>
    string Name { get; }

    /// <summary>Gets a value indicating whether this toolchain targets a Windows / MSVC host environment.</summary>
    bool IsWindows { get; }

    /// <summary>
    /// Prepares and validates the compiler environment for the specified target architectures.
    /// </summary>
    /// <param name="architectures">The target CPU architectures (e.g., <c>x64</c>, <c>arm64</c>).</param>
    /// <param name="log">The MSBuild task logging helper.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A <see cref="ToolchainPreparationResult"/> indicating whether compilation should proceed, skip, or fail.</returns>
    ToolchainPreparationResult PrepareEnvironment(
        string[] architectures,
        TaskLoggingHelper log,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the resolved environment variables for the specified architecture, or <see langword="null"/> if no custom environment is required.
    /// </summary>
    /// <param name="architecture">The target CPU architecture (e.g., <c>x64</c>, <c>arm64</c>).</param>
    /// <returns>A dictionary of environment variables, or <see langword="null"/>.</returns>
    Dictionary<string, string>? GetEnvironmentVariables(string architecture);

    /// <summary>
    /// Compiles a C++ source file into a native shared library at the specified output path.
    /// </summary>
    /// <param name="sourcePath">The full path to the input C++ source file.</param>
    /// <param name="outputPath">The full path to the output shared library file.</param>
    /// <param name="architecture">The target CPU architecture (e.g., <c>x64</c>, <c>arm64</c>).</param>
    /// <param name="log">The MSBuild task logging helper.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <param name="sanitizers">The native sanitizers (<see cref="NativeSanitizers"/>) to enable for compilation.</param>
    /// <returns><see langword="true"/> if compilation succeeded and produced the output file; otherwise, <see langword="false"/>.</returns>
    bool TryCompileCppToSharedLibrary(
        string sourcePath,
        string outputPath,
        string architecture,
        TaskLoggingHelper log,
        CancellationToken cancellationToken = default,
        NativeSanitizers sanitizers = NativeSanitizers.None);
}
