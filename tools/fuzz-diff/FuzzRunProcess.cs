using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>The outcome of one child process of <c>fuzz-diff run</c>.</summary>
/// <param name="Status">The exit code; <c>124</c> when the process timed out.</param>
/// <param name="TimedOut">Whether the process was stopped at its time limit.</param>
/// <param name="Stdout">The standard output.</param>
/// <param name="Stderr">The standard error.</param>
internal readonly record struct FuzzProcessResult(int Status, bool TimedOut, string Stdout, string Stderr);

/// <summary>Raised when <c>fuzz-diff run</c> cannot continue; the message is printed and the command exits with 1.</summary>
internal sealed class FuzzRunAbortException(string message) : Exception(message);

/// <summary>Starts the child processes of <c>fuzz-diff run</c> and reads what they print.</summary>
internal static class FuzzRunProcess
{
    /// <summary>The environment variable in which a launching script lists its own process ids, so that a run it started under the GPU lock recognizes the lock as its own.</summary>
    internal const string LauncherPidsVariable = "FUZZ_DIFF_LAUNCHER_PIDS";

    /// <summary>Runs <paramref name="fileName"/> in <paramref name="workingDirectory"/> and captures its output.</summary>
    internal static FuzzProcessResult Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory, int timeoutMs)
    {
        var psi = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        int status = FuzzWorkspaceReducer.RunProcessWithStartInfo(psi, out string stdout, out string stderr, out bool timedOut, timeoutMs);
        return new FuzzProcessResult(timedOut ? 124 : status, timedOut, stdout, stderr);
    }

    /// <summary>Returns the JSON objects among the lines of <paramref name="stdout"/>; other console lines are skipped.</summary>
    internal static List<JsonObject> ParseJsonLines(string stdout)
    {
        var events = new List<JsonObject>();
        foreach (string rawLine in stdout.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length < 2 || line[0] != '{' || line[^1] != '}')
            {
                continue;
            }

            try
            {
                if (JsonNode.Parse(line) is JsonObject node)
                {
                    events.Add(node);
                }
            }
            catch (JsonException)
            {
                // A console line that only looks like JSON is not an event.
            }
        }

        return events;
    }

    /// <summary>Returns the value of a string property, or <see langword="null"/> when it is missing or not a string.</summary>
    internal static string? GetString(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    /// <summary>Detects the compute capability of the first GPU as a <c>GSharpTargetArchitectures</c> value, or <see langword="null"/>.</summary>
    internal static string? DetectHostCudaArch(string repoRoot)
    {
        try
        {
            var result = Run("nvidia-smi", ["--query-gpu=compute_cap", "--format=csv,noheader"], repoRoot, 15_000);
            if (result.Status != 0)
            {
                return null;
            }

            foreach (string rawLine in result.Stdout.Split('\n'))
            {
                string line = rawLine.Trim();
                int dot = line.IndexOf('.');
                if (dot > 0 && dot == line.LastIndexOf('.') && IsDigits(line.AsSpan(0, dot)) && IsDigits(line.AsSpan(dot + 1)))
                {
                    string digits = line.Remove(dot, 1);
                    return $"compute_{digits},sm_{digits}";
                }
            }

            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Whether this process runs under <c>scripts/agent/gpu-lock.js</c>: the owner of the shared lock is this process or a launcher listed in <see cref="LauncherPidsVariable"/>.</summary>
    internal static bool IsAlreadyInsideGpuLock(string repoRoot)
    {
        try
        {
            var git = Run("git", ["rev-parse", "--git-common-dir"], repoRoot, 15_000);
            string commonDir = git.Stdout.Trim();
            string mainRoot = git.Status == 0 && commonDir.Length > 0
                ? Path.GetFullPath(Path.Combine(Path.GetFullPath(commonDir, repoRoot), ".."))
                : repoRoot;
            string ownerPath = Path.Combine(mainRoot, "tmp", "gpu-test-lock", "owner.json");
            if (!File.Exists(ownerPath))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(ownerPath));
            if (!document.RootElement.TryGetProperty("pid", out var pidProperty) || !pidProperty.TryGetInt32(out int ownerPid))
            {
                return false;
            }

            if (ownerPid == Environment.ProcessId)
            {
                return true;
            }

            string? launchers = Environment.GetEnvironmentVariable(LauncherPidsVariable);
            if (string.IsNullOrEmpty(launchers))
            {
                return false;
            }

            foreach (string part in launchers.Split(','))
            {
                if (int.TryParse(part, out int launcherPid) && launcherPid == ownerPid)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is JsonException or IOException or Win32Exception or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Could not read the GPU lock owner: {ex.Message}");
            return false;
        }
    }

    private static bool IsDigits(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
