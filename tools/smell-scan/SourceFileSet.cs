using System.Collections.Immutable;
using System.Text.Json;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Chooses the C# files of a run: every tracked file (<c>git ls-files</c>), the files below given paths, or the files
/// listed in a JSON array. Paths are made relative to the repository root and use <c>/</c> separators.
/// </summary>
internal static class SourceFileSet
{
    /// <summary>Lists every tracked <c>.cs</c> file of the repository at <paramref name="root"/>.</summary>
    /// <exception cref="InvalidOperationException">git could not list the files.</exception>
    public static FileSelection AllTracked(string root) => FromGit(root, []);

    /// <summary>
    /// Lists the <c>.cs</c> files that differ between <paramref name="baseRef"/> and <c>HEAD</c> of the checkout at
    /// <paramref name="root"/>, measured from their common ancestor (<c>git diff base...HEAD</c>); deleted files are left out.
    /// </summary>
    /// <exception cref="UsageException"><paramref name="baseRef"/> is not a revision of the checkout.</exception>
    public static FileSelection FromChanged(string root, string baseRef)
    {
        var range = baseRef + "...HEAD";
        var output = GitRunner.TryRun(root, "diff", "--name-only", "-z", "--diff-filter=d", range, "--", "*.cs")
            ?? throw new UsageException($"'git diff {range}' failed in '{root}'; is '{baseRef}' a revision?");
        var files = new HashSet<string>(StringComparer.Ordinal);
        for (var start = 0; start < output.Length;)
        {
            var end = output.IndexOf('\0', start);
            if (end < 0)
            {
                end = output.Length;
            }

            var relative = Normalize(output[start..end]);
            start = end + 1;
            var isSource = relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            if (isSource && File.Exists(Path.Combine(root, relative)))
            {
                files.Add(relative);
            }
        }

        return Build(files, []);
    }

    /// <summary>Lists the <c>.cs</c> files at or below <paramref name="paths"/>, each relative to the current directory.</summary>
    public static FileSelection FromPaths(string root, IReadOnlyList<string> paths)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        var missing = new HashSet<string>(StringComparer.Ordinal);
        var directories = new List<string>();
        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full))
            {
                directories.Add(Relative(root, full));
            }
            else if (File.Exists(full))
            {
                AddIfCSharp(files, Relative(root, full));
            }
            else
            {
                missing.Add(Relative(root, full));
            }
        }

        if (directories.Count > 0)
        {
            foreach (var file in FromGit(root, directories).Files)
            {
                files.Add(file);
            }
        }

        return Build(files, missing);
    }

    /// <summary>Reads a JSON array of repository-relative paths.</summary>
    /// <exception cref="InvalidDataException">The JSON is not an array of strings.</exception>
    public static FileSelection FromJsonList(string root, string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The file list must be a JSON array of paths.");
        }

        var files = new HashSet<string>(StringComparer.Ordinal);
        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("The file list must be a JSON array of paths.");
            }

            var relative = Normalize(item.GetString() ?? string.Empty);
            if (!relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(Path.Combine(root, relative)))
            {
                files.Add(relative);
            }
            else
            {
                missing.Add(relative);
            }
        }

        return Build(files, missing);
    }

    /// <summary>Returns <paramref name="path"/> with <c>/</c> separators and no leading <c>./</c>.</summary>
    public static string Normalize(string path)
    {
        var characters = path.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            if (characters[i] == '\\')
            {
                characters[i] = '/';
            }
        }

        var normalized = new string(characters);
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static FileSelection FromGit(string root, IReadOnlyList<string> pathspecs)
    {
        var arguments = new List<string> { "ls-files", "-z", "--" };
        if (pathspecs.Count == 0)
        {
            arguments.Add("*.cs");
        }
        else
        {
            foreach (var spec in pathspecs)
            {
                arguments.Add(spec.Length == 0 ? "." : spec);
            }
        }

        var output = GitRunner.TryRun(root, arguments.ToArray())
            ?? throw new InvalidOperationException($"'git ls-files' failed in '{root}'.");
        var files = new HashSet<string>(StringComparer.Ordinal);
        var missing = new HashSet<string>(StringComparer.Ordinal);
        for (var start = 0; start < output.Length;)
        {
            var end = output.IndexOf('\0', start);
            if (end < 0)
            {
                end = output.Length;
            }

            var relative = Normalize(output[start..end]);
            start = end + 1;
            if (!relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A tracked file that was deleted in the working tree is listed by git but cannot be measured.
            if (File.Exists(Path.Combine(root, relative)))
            {
                files.Add(relative);
            }
        }

        return Build(files, missing);
    }

    private static void AddIfCSharp(HashSet<string> files, string relative)
    {
        if (relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            files.Add(relative);
        }
    }

    private static string Relative(string root, string full) =>
        Normalize(Path.GetRelativePath(root, full));

    private static FileSelection Build(HashSet<string> files, HashSet<string> missing)
    {
        var sortedFiles = new List<string>(files);
        sortedFiles.Sort(StringComparer.Ordinal);
        var sortedMissing = new List<string>(missing);
        sortedMissing.Sort(StringComparer.Ordinal);
        return new FileSelection([.. sortedFiles], [.. sortedMissing]);
    }
}
