using System.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Builds start information for small shell commands that exist on every supported OS, so process tests do not depend on
/// tools such as <c>bash</c> being installed (Windows: <c>cmd.exe</c> and <c>ping.exe</c>; elsewhere: <c>sh</c> and <c>sleep</c>).
/// </summary>
internal static class ShellCommand
{
    /// <summary>Writes <paramref name="text"/> and a newline to standard output.</summary>
    public static ProcessStartInfo Echo(string text) =>
        OperatingSystem.IsWindows()
            ? Windows($"echo {text}")
            : Unix($"echo '{text}'");

    /// <summary>
    /// Writes <paramref name="lines"/> lines of <c>out line N</c> to standard output and <c>err line N</c> to standard error.
    /// </summary>
    public static ProcessStartInfo Spam(int lines) =>
        OperatingSystem.IsWindows()
            ? Windows($"\"for /L %i in (1,1,{lines}) do @(echo out line %i & echo err line %i 1>&2)\"")
            : Unix($"i=1; while [ $i -le {lines} ]; do echo out line $i; echo err line $i 1>&2; i=$((i+1)); done");

    /// <summary>Runs for about <paramref name="seconds"/> seconds without producing output.</summary>
    public static ProcessStartInfo Sleep(int seconds) =>
        OperatingSystem.IsWindows()
            ? new ProcessStartInfo { FileName = "ping.exe", Arguments = $"-n {seconds + 1} 127.0.0.1" }
            : new ProcessStartInfo { FileName = "sleep", Arguments = seconds.ToString() };

    /// <summary>Names an executable that does not exist.</summary>
    public static ProcessStartInfo Missing() => new() { FileName = "this_executable_does_not_exist" };

    private static ProcessStartInfo Windows(string command) =>
        new() { FileName = "cmd.exe", Arguments = $"/c {command}" };

    private static ProcessStartInfo Unix(string script) =>
        new() { FileName = "sh", ArgumentList = { "-c", script } };
}
