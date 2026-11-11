using System.Diagnostics;
using static Privacy;

/// <summary>
/// Runs child processes, reads files and environment variables, and keeps the reason of the last failure.
/// </summary>
internal static class ProcessProbe
{
    private const int TimeoutMilliseconds = 8000;

    // ENOENT on Linux and ERROR_FILE_NOT_FOUND on Windows: Process.Start found no such program.
    private const int FileNotFoundError = 2;

    // First stderr line of the last child process that failed (empty otherwise); the run is single-threaded.
    private static string s_lastError = string.Empty;

    internal static string LastError => s_lastError;

    /// <summary>
    /// Runs a tool and returns its trimmed standard output, or null when it is missing, fails or times out.
    /// </summary>
    internal static string? Run(string fileName, string arguments, string? workingDirectory = null)
    {
        try
        {
            s_lastError = string.Empty;
            ProcessStartInfo startInfo = CreateStartInfo(fileName, arguments, workingDirectory);
            return ExecuteProcess(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or IOException
            or AggregateException
            or NotSupportedException)
        {
            HandleProcessException(fileName, ex);
            return null;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, string arguments, string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        startInfo.Environment["VSCMD_SKIP_SENDTELEMETRY"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        return startInfo;
    }

    private static string? ExecuteProcess(ProcessStartInfo startInfo)
    {
        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        process.StandardInput.Close();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            return null;
        }

        process.WaitForExit();
        stderr.Wait(TimeoutMilliseconds);
        if (process.ExitCode != 0)
        {
            s_lastError = stderr.IsCompletedSuccessfully ? FirstLine(stderr.Result) : string.Empty;
            return null;
        }

        string output = stdout.Result.Trim();
        return output.Length == 0 ? null : output;
    }

    private static void HandleProcessException(string fileName, Exception ex)
    {
        // A tool that is not installed is shown as "not found" in the report; any other failure says why on stderr.
        s_lastError = Clean(ex.Message);
        if (ex is not System.ComponentModel.Win32Exception { NativeErrorCode: FileNotFoundError })
        {
            Warn($"cannot run {fileName}", ex);
        }
    }

    /// <summary>
    /// Writes why a value is missing to standard error, so the report on standard output stays clean.
    /// </summary>
    internal static void Warn(string what, Exception ex) =>
        Console.Error.WriteLine(Clean($"collect-env: {what}: {ex.Message}"));

    internal static string FirstLine(string text)
    {
        int end = text.IndexOf('\n');
        return (end < 0 ? text : text.Substring(0, end)).Trim();
    }

    internal static List<string> Lines(string? text)
    {
        var lines = new List<string>();
        if (text is null)
        {
            return lines;
        }

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    internal static string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn($"cannot read {path}", ex);
            return null;
        }
    }

    internal static bool IsSet(string name) => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));
}
