namespace Kassyi.GSharp.Core;

/// <summary>
/// Provides a default implementation of <see cref="ICudaPathResolver"/> using direct file system access and OS inspection.
/// </summary>
public class DefaultCudaPathResolver : ICudaPathResolver
{
    // Virtual I/O boundaries for mocking in unit tests

    /// <summary>
    /// Retrieves the value of an environment variable.
    /// </summary>
    /// <param name="name">The name of the environment variable.</param>
    /// <returns>The value of the environment variable, or <see langword="null"/> if not found.</returns>
    protected virtual string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    /// <summary>
    /// Determines whether the given directory path refers to an existing directory on disk.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> if the path refers to an existing directory; otherwise, <see langword="false"/>.</returns>
    protected virtual bool DirectoryExists(string path) => Directory.Exists(path);

    /// <summary>
    /// Returns the names of subdirectories matching a search pattern in a specified directory.
    /// </summary>
    /// <param name="path">The relative or absolute path to the directory to search.</param>
    /// <param name="searchPattern">The search string to match against the names of subdirectories.</param>
    /// <returns>An array of full names of matching directories.</returns>
    protected virtual string[] GetDirectories(string path, string searchPattern) => Directory.GetDirectories(path, searchPattern);

    /// <summary>
    /// Determines whether the current operating system is Windows.
    /// </summary>
    /// <returns><see langword="true"/> if running on Windows; otherwise, <see langword="false"/>.</returns>
    protected virtual bool IsWindows() => OperatingSystem.IsWindows();

    /// <summary>
    /// Determines whether the current operating system is Linux.
    /// </summary>
    /// <returns><see langword="true"/> if running on Linux; otherwise, <see langword="false"/>.</returns>
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
            const string BaseDir = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
            if (!DirectoryExists(BaseDir))
            {
                return null;
            }

            var subDirs = GetDirectories(BaseDir, "v*");
            if (subDirs.Length <= 0)
            {
                return null;
            }

            string? bestPath = null;
            Version? bestVersion = null;

            foreach (var d in subDirs)
            {
                var folderName = Path.GetFileName(d);
                var versionStr = folderName.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? folderName[1..]
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
