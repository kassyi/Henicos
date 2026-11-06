using System.Collections.Concurrent;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Finds the project a file belongs to: the nearest directory above it that holds a project file (<c>.csproj</c> or
/// <c>.shproj</c>). A file under no project (shared sources that projects link in) belongs to its first two folders,
/// such as <c>src/Shared</c>, or to its first folder when it sits directly in one.
/// </summary>
internal sealed class ProjectLocator(string root)
{
    private readonly ConcurrentDictionary<string, string?> _byDirectory = new(StringComparer.Ordinal);

    /// <summary>Returns the repository-relative directory of the project of <paramref name="path"/>.</summary>
    public string Find(string path)
    {
        var slash = path.LastIndexOf('/');
        if (slash < 0)
        {
            return ".";
        }

        return FindFromDirectory(path[..slash]) ?? Fallback(path);
    }

    private static string Fallback(string path)
    {
        var first = path.IndexOf('/');
        var second = path.IndexOf('/', first + 1);
        return second < 0 ? path[..first] : path[..second];
    }

    private string? FindFromDirectory(string directory)
    {
        if (_byDirectory.TryGetValue(directory, out var known))
        {
            return known;
        }

        var parentSlash = directory.LastIndexOf('/');
        var found = HasProjectFile(directory)
            ? directory
            : parentSlash < 0 ? null : FindFromDirectory(directory[..parentSlash]);
        _byDirectory[directory] = found;
        return found;
    }

    private bool HasProjectFile(string directory)
    {
        var full = Path.Combine(root, directory);
        if (!Directory.Exists(full))
        {
            return false;
        }

        foreach (var pattern in new[] { "*.csproj", "*.shproj" })
        {
            foreach (var _ in Directory.EnumerateFiles(full, pattern))
            {
                return true;
            }
        }

        return false;
    }
}
