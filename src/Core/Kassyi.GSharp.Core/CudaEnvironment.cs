namespace Kassyi.GSharp.Core;

/// <summary>
/// Provides utility methods for discovering and resolving local NVIDIA CUDA Toolkit installation directories.
/// </summary>
/// <remarks>
/// This utility queries system environment variables and default OS filesystem paths across Windows and Linux.
/// </remarks>
public static class CudaEnvironment
{
    /// <summary>
    /// Gets or sets the active CUDA path resolver strategy. Defaults to <see cref="DefaultCudaPathResolver"/>.
    /// </summary>
    public static ICudaPathResolver Resolver { get; set; } = new DefaultCudaPathResolver();

    /// <summary>
    /// Discovers and returns the root installation directory of the NVIDIA CUDA Toolkit.
    /// </summary>
    /// <returns>
    /// The absolute path to the CUDA installation directory if located; otherwise, <see langword="null"/>.
    /// </returns>
    public static string? GetCudaPath() => Resolver.GetCudaPath();

    /// <summary>
    /// Discovers and returns the path to the <c>bin</c> directory of the CUDA Toolkit containing binaries and dynamic libraries.
    /// </summary>
    /// <returns>
    /// The absolute path to the CUDA <c>bin</c> directory (or <c>bin/x64</c> on Windows if present); otherwise, <see langword="null"/>.
    /// </returns>
    public static string? GetCudaBinPath() => Resolver.GetCudaBinPath();
}
