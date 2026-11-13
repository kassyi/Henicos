namespace Kassyi.GSharp.Core;

/// <summary>
/// Defines a contract for discovering the NVIDIA CUDA Toolkit installation paths.
/// </summary>
public interface ICudaPathResolver
{
    /// <summary>
    /// Discovers and returns the root installation directory of the NVIDIA CUDA Toolkit.
    /// </summary>
    /// <returns>The absolute path to the CUDA installation directory if located; otherwise, <see langword="null"/>.</returns>
    string? GetCudaPath();

    /// <summary>
    /// Discovers and returns the path to the <c>bin</c> directory of the CUDA Toolkit containing binaries and dynamic libraries.
    /// </summary>
    /// <returns>The absolute path to the CUDA <c>bin</c> directory (or <c>bin/x64</c> on Windows if present); otherwise, <see langword="null"/>.</returns>
    string? GetCudaBinPath();
}
