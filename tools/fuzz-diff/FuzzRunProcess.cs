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

/// <summary>Raised when <c>fuzz-diff run</c> cannot continue; the message is printed and the exit code is 1.</summary>
internal sealed class FuzzRunAbortException(string message) : Exception(message);

/// <summary>Starts the child processes of the <c>fuzz-diff</c> commands and reads what they print.</summary>
internal static class FuzzRunProcess
{
    /// <summary>
    /// The environment variable in which a launching script lists its own process ids, so that a run it started under
    /// the GPU lock recognizes the lock as its own.
    /// </summary>
    internal const string LauncherPidsVariable = "FUZZ_DIFF_LAUNCHER_PIDS";

    /// <summary>The exit code of <c>scripts/agent/gpu-lock.js</c> when it gave up waiting for the lock.</summary>
    internal const int GpuLockWaitTimeoutStatus = 3;

    /// <summary>Runs <paramref name="fileName"/> in <paramref name="workingDirectory"/>; captures its output.</summary>
    internal static FuzzProcessResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int timeoutMs)
    {
        var psi = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        int status = RunWithStartInfo(psi, out string stdout, out string stderr, out bool timedOut, timeoutMs);
        return new FuzzProcessResult(status, timedOut, stdout, stderr);
    }

    /// <summary>
    /// Starts <paramref name="psi"/> with its output redirected and waits up to <paramref name="timeoutMs"/>; on a
    /// timeout the process tree is stopped and <c>124</c> is returned. Every child gets <c>DiffEngine_Disabled</c>,
    /// <c>CI</c> and <c>DOTNET_NOLOGO</c>, so that a test run never opens a diff viewer.
    /// </summary>
    internal static int RunWithStartInfo(
        ProcessStartInfo psi,
        out string stdout,
        out string stderr,
        out bool timedOut,
        int timeoutMs = 120_000)
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.Environment["DiffEngine_Disabled"] = "true";
        psi.Environment["CI"] = "true";
        psi.Environment["DOTNET_NOLOGO"] = "1";

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {psi.FileName}.");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(timeoutMs))
        {
            timedOut = true;
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            // smell-scan: ignore swallowed-exception -- the process may exit between the timeout and Kill, or a child
            // of it may refuse to stop; the caller already treats the run as timed out
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or AggregateException)
            {
                Trace.WriteLine($"Could not kill {psi.FileName} after the timeout: {ex.Message}");
            }

            proc.WaitForExit(5_000);
            stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
            stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
            return 124;
        }

        proc.WaitForExit();
        timedOut = false;
        stdout = stdoutTask.GetAwaiter().GetResult();
        stderr = stderrTask.GetAwaiter().GetResult();
        return proc.ExitCode;
    }

    /// <summary>
    /// Whether a run through <c>scripts/agent/gpu-lock.js</c> ended because the lock wait timed out: the script exits
    /// with <see cref="GpuLockWaitTimeoutStatus"/> and says it gave up, so a command that exits with the same code is
    /// not mistaken for it.
    /// </summary>
    internal static bool IsGpuLockWaitTimeout(FuzzProcessResult result) =>
        !result.TimedOut &&
        result.Status == GpuLockWaitTimeoutStatus &&
        result.Stderr.Contains("[gpu-lock] gave up", StringComparison.Ordinal);

    /// <summary>Returns the JSON objects among the lines of <paramref name="stdout"/>, skipping the rest.</summary>
    internal static List<JsonObject> ParseJsonLines(string stdout)
    {
        var events = new List<JsonObject>();
        foreach (Range range in stdout.AsSpan().Split('\n'))
        {
            ReadOnlySpan<char> line = stdout.AsSpan(range).Trim();
            if (line.Length < 2 || line[0] != '{' || line[^1] != '}')
            {
                continue;
            }

            try
            {
                if (JsonNode.Parse(line.ToString()) is JsonObject node)
                {
                    events.Add(node);
                }
            }
            // smell-scan: ignore swallowed-exception -- a console line that only looks like JSON is not an event; the
            // loop goes on with the next line
            catch (JsonException ex)
            {
                Trace.WriteLine($"Skipped a console line that is not JSON: {ex.Message}");
            }
        }

        return events;
    }

    /// <summary>Returns a string property, or <see langword="null"/> when it is missing or not a string.</summary>
    internal static string? GetString(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    /// <summary>
    /// Detects the compute capability of the first GPU as a <c>GSharpTargetArchitectures</c> value, or
    /// <see langword="null"/>.
    /// </summary>
    internal static string? DetectHostCudaArch(string repoRoot)
    {
        try
        {
            var result = Run("nvidia-smi", ["--query-gpu=compute_cap", "--format=csv,noheader"], repoRoot, 15_000);
            if (result.Status != 0)
            {
                return null;
            }

            foreach (Range range in result.Stdout.AsSpan().Split('\n'))
            {
                ReadOnlySpan<char> line = result.Stdout.AsSpan(range).Trim();
                int dot = line.IndexOf('.');
                if (dot > 0 &&
                    dot == line.LastIndexOf('.') &&
                    IsDigits(line[..dot]) &&
                    IsDigits(line[(dot + 1)..]))
                {
                    string digits = string.Concat(line[..dot], line[(dot + 1)..]);
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

    /// <summary>
    /// Whether this process runs under <c>scripts/agent/gpu-lock.js</c>: the owner of the shared lock of the main
    /// checkout of <paramref name="repoRoot"/> is this process or a launcher listed in
    /// <see cref="LauncherPidsVariable"/>.
    /// </summary>
    internal static bool IsAlreadyInsideGpuLock(string repoRoot)
    {
        try
        {
            var git = Run("git", ["rev-parse", "--git-common-dir"], repoRoot, 15_000);
            string commonDir = git.Stdout.AsSpan().Trim().ToString();
            string mainRoot = git.Status == 0 && commonDir.Length > 0
                ? Path.GetFullPath(Path.Combine(Path.GetFullPath(commonDir, repoRoot), ".."))
                : repoRoot;
            string ownerPath = Path.Combine(mainRoot, "tmp", "gpu-test-lock", "owner.json");
            return IsAlreadyInsideGpuLock(
                ownerPath,
                Environment.ProcessId,
                Environment.GetEnvironmentVariable(LauncherPidsVariable));
        }
        catch (Win32Exception ex)
        {
            Trace.WriteLine($"Could not find the GPU lock: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Whether the lock owner recorded in <paramref name="ownerPath"/> is <paramref name="processId"/> or one of the
    /// comma-separated <paramref name="launcherPids"/>. A missing or unreadable owner file is no lock of ours.
    /// </summary>
    internal static bool IsAlreadyInsideGpuLock(string ownerPath, int processId, string? launcherPids)
    {
        try
        {
            if (!File.Exists(ownerPath))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(ownerPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("pid", out var pidProperty) ||
                !pidProperty.TryGetInt32(out int ownerPid))
            {
                return false;
            }

            return ownerPid == processId || IsListed(ownerPid, launcherPids);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Could not read the GPU lock owner: {ex.Message}");
            return false;
        }
    }

    private static bool IsListed(int pid, string? pids)
    {
        if (string.IsNullOrEmpty(pids))
        {
            return false;
        }

        foreach (Range range in pids.AsSpan().Split(','))
        {
            if (int.TryParse(pids.AsSpan(range), out int listed) && listed == pid)
            {
                return true;
            }
        }

        return false;
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
