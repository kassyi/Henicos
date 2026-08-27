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
    /// Discovers and returns the root installation directory of the NVIDIA CUDA Toolkit.
    /// </summary>
    /// <returns>
    /// The absolute path to the CUDA installation directory if located; otherwise, <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Searches the <c>CUDA_PATH</c> environment variable first, then inspects standard OS directories (e.g. <c>C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA</c> on Windows or <c>/usr/local/cuda</c> on Linux).
    /// </remarks>
    public static string? GetCudaPath()
    {
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrEmpty(cudaPath) && Directory.Exists(cudaPath))
        {
            return cudaPath;
        }

        if (OperatingSystem.IsWindows())
        {
            const string BaseDir = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
            if (!Directory.Exists(BaseDir))
            {
                return null;
            }

            var subDirs = Directory.GetDirectories(BaseDir, "v*");
            if (subDirs.Length > 0)
            {
                return subDirs
                    .Select(d => new
                    {
                        Path = d,
                        Version = Version.TryParse(Path.GetFileName(d).TrimStart('v'), out var v)
                            ? v
                            : new Version(0, 0)
                    })
                    .OrderByDescending(x => x.Version)
                    .Select(x => x.Path)
                    .FirstOrDefault();
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            if (Directory.Exists("/usr/local/cuda"))
            {
                return "/usr/local/cuda";
            }
        }

        return null;
    }

    /// <summary>
    /// Discovers and returns the path to the <c>bin</c> directory of the CUDA Toolkit containing binaries and dynamic libraries.
    /// </summary>
    /// <returns>
    /// The absolute path to the CUDA <c>bin</c> directory (or <c>bin/x64</c> on Windows if present); otherwise, <see langword="null"/>.
    /// </returns>
    public static string? GetCudaBinPath()
    {
        var cudaPath = GetCudaPath();
        if (cudaPath != null)
        {
            var binPath = Path.Combine(cudaPath, "bin");
            if (OperatingSystem.IsWindows())
            {
                var binX64 = Path.Combine(binPath, "x64");
                if (Directory.Exists(binX64))
                {
                    return binX64;
                }
            }

            if (Directory.Exists(binPath))
            {
                return binPath;
            }
        }

        return null;
    }
}
