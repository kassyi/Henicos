using static Privacy;
using static ProcessProbe;

/// <summary>Reads the G# checkout and the G# environment variables.</summary>
internal static class CheckoutProbe
{
    internal static string? FindUp(string fileName)
    {
        try
        {
            DirectoryInfo? current = new(Directory.GetCurrentDirectory());
            while (current is not null)
            {
                string candidate = Path.Combine(current.FullName, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn($"cannot search the parent directories for {fileName}", ex);
        }

        return null;
    }

    internal static string? GSharpCheckout()
    {
        string? slnx = FindUp("Henicos.slnx");
        if (slnx is null)
        {
            return null;
        }

        string repo = Path.GetDirectoryName(slnx) ?? Directory.GetCurrentDirectory();
        string? commit = Run("git", "rev-parse --short HEAD", repo);
        if (commit is null)
        {
            // Inside a clone, so say why git failed (dubious ownership, a worktree whose .git file points to another
            // OS, ...).
            string reason = LastError.Length > 0 ? LastError : "no output";
            return Clean($"git failed: {reason}");
        }

        string branch = Run("git", "branch --show-current", repo) ?? "detached";
        string? porcelain = Run("git", "status --porcelain", repo);
        return Clean($"commit {FirstLine(commit)} ({FirstLine(branch)}, {(porcelain is null ? "clean" : "dirty")})");
    }

    internal static void CollectEnvironmentVariables(ReportModel model)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal) { "GSHARP_TEST_NO_CUDA", "CUDA_VISIBLE_DEVICES" };
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && key.StartsWith("GSHARP_", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(key);
            }
        }

        foreach (string name in names)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            model.EnvironmentVariables[name] = string.IsNullOrEmpty(value) ? "(unset)" : Clean(value);
        }
    }
}
