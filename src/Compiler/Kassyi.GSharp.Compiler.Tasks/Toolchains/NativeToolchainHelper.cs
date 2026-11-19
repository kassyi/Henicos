using System.Text;

namespace Kassyi.GSharp.Compiler.Tasks.Toolchains;

/// <summary>
/// Shared helpers for native C++ toolchains (single-unit fallback, PATH lookup, and argument joining).
/// </summary>
internal static class NativeToolchainHelper
{
    public static bool TryCompileSingleSourceToSharedLibrary(
        INativeToolchain toolchain,
        string sourcePath,
        string outputPath,
        string architecture,
        TaskLoggingHelper log,
        CancellationToken cancellationToken,
        NativeSanitizers sanitizers)
    {
        var baseDir = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(outputPath);
        var objExt = toolchain.IsWindows ? ".obj" : ".o";
        var objPath = Path.Combine(baseDir, baseName + objExt);
        string? vcPdbPath = toolchain.IsWindows ? Path.Combine(baseDir, baseName + ".vc.pdb") : null;

        try
        {
            if (!toolchain.TryCompileToObject(
                    sourcePath, objPath, architecture, log, cancellationToken, sanitizers, vcPdbPath))
            {
                return false;
            }

            return toolchain.TryLinkSharedLibrary(
                [objPath], outputPath, architecture, log, cancellationToken, sanitizers);
        }
        finally
        {
            CompileKernelTaskBase.CleanupFiles(log, objPath, vcPdbPath ?? string.Empty);
        }
    }

    public static string FindExecutableInPath(string executableName, Dictionary<string, string>? env)
    {
        if (Path.IsPathRooted(executableName))
        {
            return executableName;
        }

        string? pathEnv = null;
        if (env is not null)
        {
            env.TryGetValue("PATH", out pathEnv);
        }
        else
        {
            pathEnv = Environment.GetEnvironmentVariable("PATH");
        }

        if (string.IsNullOrEmpty(pathEnv))
        {
            return executableName;
        }

        var span = pathEnv.AsSpan();
        while (span.Length > 0)
        {
            var sepIdx = span.IndexOf(Path.PathSeparator);
            var segment = (sepIdx >= 0 ? span.Slice(0, sepIdx) : span).Trim().Trim('"');
            if (segment.Length > 0)
            {
                var fullPath = Path.Combine(segment.ToString(), executableName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            if (sepIdx < 0)
            {
                break;
            }

            span = span.Slice(sepIdx + 1);
        }

        return executableName;
    }

    public static string JoinQuotedPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return string.Empty;
        }

        var quoted = new string[paths.Count];
        for (var i = 0; i < paths.Count; i++)
        {
            quoted[i] = $"\"{paths[i]}\"";
        }

        return string.Join(" ", quoted);
    }

    public static bool HasNonEmptyFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Treat unreadable file metadata as absent so the caller does not skip or trust an invalid output.
            return false;
        }
    }

    /// <summary>
    /// The values of <paramref name="names"/> as <c>NAME=value</c> joined by tabs, read from <paramref name="env"/>
    /// (the environment the compiler is started with) or, without one, from this process; an unset variable is empty.
    /// </summary>
    public static string DescribeEnvironment(Dictionary<string, string>? env, params string[] names)
    {
        var builder = new StringBuilder();
        foreach (var name in names)
        {
            // The compiler starts with the process environment overlaid by env, so a name env lacks comes from the
            // process
            var value = env is not null && env.TryGetValue(name, out var set)
                ? set
                : Environment.GetEnvironmentVariable(name) ?? string.Empty;
            if (builder.Length > 0)
            {
                builder.Append('\t');
            }

            builder.Append(name).Append('=').Append(value);
        }

        return builder.ToString();
    }

    public static void EnsureParentDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
