#nullable enable
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Resolves repository files from test code by walking up to <c>Henicos.slnx</c>, so tests never depend on a fixed
/// <c>../</c> depth from the build output or on where their project sits under <c>tests/</c>.
/// </summary>
public static class RepositoryPaths
{
    private const string RootMarker = "Henicos.slnx";

    /// <summary>
    /// Gets the repository root, the directory that contains <c>Henicos.slnx</c>.
    /// </summary>
    public static string Root { get; } = FindRoot();

    /// <summary>
    /// Gets the directory of the <c>Kassyi.GSharp.Compiler</c> project (<c>src/Compiler/Kassyi.GSharp.Compiler</c>).
    /// </summary>
    public static string CompilerProject => Combine("src", "Compiler", "Kassyi.GSharp.Compiler");

    /// <summary>
    /// Combines <paramref name="segments"/> onto <see cref="Root"/>.
    /// </summary>
    /// <param name="segments">Path segments relative to the repository root.</param>
    /// <returns>The absolute path.</returns>
    public static string Combine(params string[] segments)
    {
        var parts = new string[segments.Length + 1];
        parts[0] = Root;
        Array.Copy(segments, 0, parts, 1, segments.Length);
        return Path.Combine(parts);
    }

    // The source path works for every test assembly built in this checkout; the output directory is the fallback
    // when the binaries were built elsewhere.
    private static string FindRoot([CallerFilePath] string thisFilePath = "")
    {
        return FindRootAbove(Path.GetDirectoryName(thisFilePath))
               ?? FindRootAbove(AppContext.BaseDirectory)
               ?? throw new DirectoryNotFoundException($"Could not locate the repository root ({RootMarker}).");
    }

    private static string? FindRootAbove(string? startDirectory)
    {
        if (startDirectory is null or "")
        {
            return null;
        }

        for (var dir = new DirectoryInfo(startDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, RootMarker)))
            {
                return dir.FullName;
            }
        }

        return null;
    }
}
