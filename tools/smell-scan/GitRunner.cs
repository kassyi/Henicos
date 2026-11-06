using System.Diagnostics;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>Runs <c>git</c> in a directory and returns what it prints.</summary>
internal static class GitRunner
{
    /// <summary>Runs <c>git</c> with <paramref name="arguments"/> in <paramref name="directory"/>.</summary>
    /// <returns>The standard output, or <see langword="null"/> when git exits with an error or cannot start.</returns>
    public static string? TryRun(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            error.Wait();
            return process.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // git is not installed; the caller reports the missing file list.
            return null;
        }
    }
}
