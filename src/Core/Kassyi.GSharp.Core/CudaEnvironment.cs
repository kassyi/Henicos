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

/// <summary>
/// Provides a default implementation of <see cref="ICudaPathResolver"/> using direct file system access and OS inspection.
/// </summary>
public class DefaultCudaPathResolver : ICudaPathResolver
{
    // Virtual I/O boundaries for mocking in unit tests
    protected virtual string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
    protected virtual bool DirectoryExists(string path) => Directory.Exists(path);
    protected virtual string[] GetDirectories(string path, string searchPattern) => Directory.GetDirectories(path, searchPattern);
    protected virtual bool IsWindows() => OperatingSystem.IsWindows();
    protected virtual bool IsLinux() => OperatingSystem.IsLinux();

    /// <inheritdoc />
    public virtual string? GetCudaPath()
    {
        var cudaPath = GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrEmpty(cudaPath) && DirectoryExists(cudaPath))
        {
            return cudaPath;
        }

        if (IsWindows())
        {
            const string baseDir = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
            if (!DirectoryExists(baseDir))
            {
                return null;
            }

            var subDirs = GetDirectories(baseDir, "v*");
            if (subDirs.Length > 0)
            {
                string? bestPath = null;
                Version? bestVersion = null;

                foreach (var d in subDirs)
                {
                    var folderName = Path.GetFileName(d);
                    var versionStr = folderName.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                        ? folderName.Substring(1)
                        : folderName;

                    if (Version.TryParse(versionStr, out var v))
                    {
                        if (bestVersion != null && v <= bestVersion)
                        {
                            continue;
                        }

                        bestVersion = v;
                        bestPath = d;
                    }
                    else if (bestPath == null)
                    {
                        bestPath = d;
                    }
                }

                return bestPath;
            }
        }
        else if (IsLinux())
        {
            if (DirectoryExists("/usr/local/cuda"))
            {
                return "/usr/local/cuda";
            }
        }

        return null;
    }

    /// <inheritdoc />
    public virtual string? GetCudaBinPath()
    {
        var cudaPath = GetCudaPath();
        if (cudaPath != null)
        {
            var binPath = Path.Combine(cudaPath, "bin");
            if (IsWindows())
            {
                var binX64 = Path.Combine(binPath, "x64");
                if (DirectoryExists(binX64))
                {
                    return binX64;
                }
            }

            if (DirectoryExists(binPath))
            {
                return binPath;
            }
        }

        return null;
    }
}

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
