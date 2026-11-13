using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>
/// Starts a process on Linux and macOS as the leader of its own session and process group (through the
/// <c>setsid</c> program), so one signal to the group stops every process it starts.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> <see cref="Process"/> has no option to start a child in a new group, and the parent cannot move the
/// child afterwards (<c>setpgid</c> fails once the child has called <c>exec</c>, which <see cref="Process.Start()"/>
/// waits for). <c>setsid</c> calls <c>setsid()</c> and then executes the program in the same process, so the process
/// ID, the exit code and the pipes are those of the program itself, and the group exists before the program can start
/// anything.
/// </para>
/// <para>
/// The program path is resolved before wrapping in the order <see cref="Process.Start()"/> uses (a rooted path as
/// it is, else the host's directory, the current directory, then <c>PATH</c>), so <c>setsid</c> runs the same file.
/// Without <c>setsid</c> (macOS has no such program), with a program that cannot be found (so that
/// <see cref="Process.Start()"/> reports it as before), or with a start info whose arguments are in
/// <c>ArgumentList</c> (not visible to <c>netstandard2.0</c>), the process starts in the caller's group and a
/// cancellation stops only that process.
/// </para>
/// </remarks>
internal static class UnixProcessGroup
{
    private const int SigKill = 9;

    // The directory of the running host, which Process.Start searches before the current directory and PATH.
    private static readonly Lazy<string?> s_hostDirectory = new(static () =>
    {
        using var current = Process.GetCurrentProcess();
        return current.MainModule?.FileName is { } host ? Path.GetDirectoryName(host) : null;
    });

    private static readonly string? s_setsid = FindSetsid();

    /// <summary>
    /// Points <paramref name="startInfo"/> at <c>setsid</c> with the resolved program as its first argument, or leaves
    /// it unchanged and returns <see langword="false"/>.
    /// </summary>
    public static bool TryWrap(ProcessStartInfo startInfo, out OriginalCommand original)
    {
        original = new OriginalCommand(startInfo.FileName, startInfo.Arguments);
        if (s_setsid is null || ResolveProgram(startInfo.FileName) is not { } program)
        {
            return false;
        }

        startInfo.FileName = s_setsid;
        startInfo.Arguments = startInfo.Arguments.Length == 0
            ? CommandLineQuoting.Quote(program)
            : CommandLineQuoting.Quote(program) + " " + startInfo.Arguments;
        return true;
    }

    /// <summary>Kills the group led by <paramref name="leader"/> after the leader itself.</summary>
    /// <remarks>
    /// The leader is killed first through <see cref="Process.Kill()"/>, which never signals a reaped and reused
    /// process ID; a process with a pending kill cannot start another one, so the group cannot grow afterwards. The
    /// group ID stays reserved while any member lives, so the group signal reaches only the run's own descendants.
    /// </remarks>
    public static void Kill(Process leader)
    {
        var leaderId = leader.Id;
        ProcessTree.KillProcessOnly(leader);

        // ESRCH (no member left, or setsid had not run yet and the leader died before it) needs no handling.
        _ = SendSignal(-leaderId, SigKill);
    }

    private static string? ResolveProgram(string fileName)
    {
        if (fileName.Length == 0)
        {
            return null;
        }

        if (Path.IsPathRooted(fileName))
        {
            return File.Exists(fileName) ? fileName : null;
        }

        return ExistingIn(s_hostDirectory.Value, fileName)
            ?? ExistingIn(Directory.GetCurrentDirectory(), fileName)
            ?? FindInPath(fileName);
    }

    private static string? FindSetsid()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        return ExistingIn("/usr/bin", "setsid") ?? ExistingIn("/bin", "setsid") ?? FindInPath("setsid");
    }

    private static string? FindInPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var rest = path.AsSpan();
        while (rest.Length > 0)
        {
            var end = rest.IndexOf(':');
            var directory = end < 0 ? rest : rest.Slice(0, end);
            rest = end < 0 ? ReadOnlySpan<char>.Empty : rest.Slice(end + 1);
            if (directory.Length > 0 && ExistingIn(directory.ToString(), fileName) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string? ExistingIn(string? directory, string fileName)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var candidate = Path.Combine(directory, fileName);
        return File.Exists(candidate) ? candidate : null;
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SendSignal(int processId, int signal);

    /// <summary>The program and arguments of a start info before <see cref="TryWrap"/> changed them.</summary>
    internal readonly struct OriginalCommand(string fileName, string arguments)
    {
        /// <summary>Puts the program and arguments back.</summary>
        public void Restore(ProcessStartInfo startInfo)
        {
            startInfo.FileName = fileName;
            startInfo.Arguments = arguments;
        }
    }
}
