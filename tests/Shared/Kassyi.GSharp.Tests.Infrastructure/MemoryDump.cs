#nullable enable
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Writes a memory snapshot of the current test host with the .NET diagnostic tools, run one-shot through
/// <c>dotnet dnx</c> at a pinned version (no global install).
/// </summary>
/// <remarks>
/// <see cref="DumpVariable"/> chooses the kind: <c>gcdump</c> (default) writes a managed heap snapshot with
/// <c>dotnet-gcdump collect</c> and its <c>dotnet-gcdump report</c> (types by size) next to it; <c>full</c> writes a
/// full process dump with <c>dotnet-dump collect --type Full</c>; <c>none</c> writes nothing. Output goes to a
/// directory <c>&lt;assembly&gt;-&lt;time&gt;-&lt;pid&gt;</c> under <c>test-memory</c> in the repository's scratch
/// directory, which CI uploads when a job fails.
/// </remarks>
public static class MemoryDump
{
    /// <summary>
    /// The environment variable that chooses the snapshot kind: <c>gcdump</c> (default), <c>full</c> or <c>none</c>.
    /// </summary>
    public const string DumpVariable = "GSHARP_TEST_MEMORY_DUMP";

    /// <summary>
    /// The version of <c>dotnet-gcdump</c> and <c>dotnet-dump</c> that <c>dotnet dnx</c> runs.
    /// </summary>
    public const string ToolVersion = "10.0.750501";

    /// <summary>
    /// The longest a snapshot (tool download included) may take before it is abandoned.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(180);

    private static readonly TimeSpan s_reportTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Writes a snapshot of the current process as <see cref="DumpVariable"/> chooses.
    /// </summary>
    /// <param name="assemblyName">The test assembly name, used in the directory name.</param>
    /// <param name="label">A short label for the file names, such as <c>breach</c> or <c>end</c>.</param>
    /// <returns>One sentence naming the files written, or why none was.</returns>
    public static string Collect(string assemblyName, string label)
    {
        string kind = Environment.GetEnvironmentVariable(DumpVariable) is { Length: > 0 } value ? value : "gcdump";
        if (kind == "none")
        {
            return $"No memory snapshot ({DumpVariable}=none).";
        }

        if (kind != "gcdump" && kind != "full")
        {
            return $"No memory snapshot: {DumpVariable} must be gcdump, full or none, got '{kind}'.";
        }

        int pid = Environment.ProcessId;
        string directory = RepositoryPaths.Combine(
            "tmp",
            "test-memory",
            $"{assemblyName}-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{pid}"
        );
        Directory.CreateDirectory(directory);
        string pidText = pid.ToString(CultureInfo.InvariantCulture);
        if (kind == "full")
        {
            string dump = Path.Combine(directory, $"{label}.dmp");
            string[] dumpArguments = ["collect", "-p", pidText, "--type", "Full", "-o", dump];
            string? dumpError = RunTool("dotnet-dump", dumpArguments, Timeout, out _);
            return dumpError is null ? $"Full dump: {dump}." : $"Full dump to {dump} failed: {dumpError}";
        }

        string gcdump = Path.Combine(directory, $"{label}.gcdump");
        string seconds = ((int)Timeout.TotalSeconds - 10).ToString(CultureInfo.InvariantCulture);
        string[] collectArguments = ["collect", "-p", pidText, "-o", gcdump, "-t", seconds];
        string? error = RunTool("dotnet-gcdump", collectArguments, Timeout, out _);
        if (error is not null)
        {
            return $"Heap snapshot to {gcdump} failed: {error}";
        }

        string report = Path.ChangeExtension(gcdump, ".report.txt");
        string? reportError = RunTool("dotnet-gcdump", ["report", gcdump], s_reportTimeout, out string reportText);
        if (reportError is not null)
        {
            return $"Heap snapshot: {gcdump} (its report failed: {reportError}).";
        }

        File.WriteAllText(report, reportText);
        return $"Heap snapshot: {gcdump}, types by size: {report}.";
    }

    // Runs `dotnet dnx <tool>@<version> --yes -- <arguments>`; returns null on success, otherwise the reason
    private static string? RunTool(string tool, string[] arguments, TimeSpan timeout, out string output)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepositoryPaths.Root,
        };
        foreach (string argument in new[] { "dnx", $"{tool}@{ToolVersion}", "--yes", "--" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        var text = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => Append(text, e.Data);
        process.ErrorDataReceived += (_, e) => Append(text, e.Data);
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            output = string.Empty;
            return $"could not start dotnet ({ex.Message}).";
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            output = Snapshot(text);
            return $"{tool} did not finish in {timeout.TotalSeconds:0} s.";
        }

        process.WaitForExit();
        output = Snapshot(text);
        return process.ExitCode == 0 ? null : $"{tool} exited with {process.ExitCode}: {LastLines(output)}";
    }

    private static void Append(StringBuilder text, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (text)
        {
            text.AppendLine(line);
        }
    }

    private static string Snapshot(StringBuilder text)
    {
        lock (text)
        {
            return text.ToString();
        }
    }

    private static string LastLines(string output)
    {
        ReadOnlySpan<char> trimmed = output.AsSpan().TrimEnd();
        int start = trimmed.Length;
        for (int lines = 0; start > 0 && lines < 3; lines++)
        {
            start = trimmed[..(start - 1)].LastIndexOf('\n') + 1;
        }

        return trimmed[start..].ToString().ReplaceLineEndings(" | ");
    }
}
