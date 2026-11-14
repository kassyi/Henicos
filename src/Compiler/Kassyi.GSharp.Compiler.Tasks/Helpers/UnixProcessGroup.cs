using System.Runtime.InteropServices;
using System.Text;

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
/// The program path is resolved before wrapping as <see cref="Process.Start()"/> resolves it (a rooted path as it
/// is, else the host's directory, the current directory, then the first executable file of that name on
/// <c>PATH</c>), so <c>setsid</c> runs the same file. The program is wrapped only when <c>exec</c> will accept it:
/// it has the execute permission and starts with the ELF magic or with <c>#!</c> naming an executable interpreter.
/// Otherwise <see cref="Process.Start()"/> starts it unwrapped and reports a failure with the same
/// <see cref="System.ComponentModel.Win32Exception"/> as before, instead of an exit code of <c>setsid</c> or the
/// <c>/bin/sh</c> fallback of its <c>execvp</c>.
/// </para>
/// <para>
/// Without <c>setsid</c> (macOS has no such program), with a program that is not wrapped, or with a start info whose
/// arguments are in <c>ArgumentList</c> (not visible to <c>netstandard2.0</c>), the process starts in the caller's
/// group, and <see cref="UnixDescendants"/> stops its tree on cancellation.
/// </para>
/// </remarks>
internal static class UnixProcessGroup
{
    private const byte Tab = 0x09;
    private const byte LineFeed = 0x0A;
    private const byte CarriageReturn = 0x0D;
    private const byte Space = 0x20;

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
        if (s_setsid is null || ResolveProgram(startInfo.FileName) is not { } program || !ExecAccepts(program))
        {
            return false;
        }

        startInfo.FileName = s_setsid;
        startInfo.Arguments = startInfo.Arguments.Length == 0
            ? CommandLineQuoting.Quote(program)
            : CommandLineQuoting.Quote(program) + " " + startInfo.Arguments;
        return true;
    }

    /// <summary>
    /// Kills the group led by <paramref name="leader"/> (process ID <paramref name="leaderId"/>), leader first.
    /// </summary>
    /// <remarks>
    /// The leader is killed first through <see cref="Process.Kill()"/>, which never signals a reaped and reused
    /// process ID; a process with a pending kill cannot start another one, so the group cannot grow afterwards. The
    /// group ID stays reserved while any member lives, so the group signal reaches only the run's own descendants.
    /// </remarks>
    public static void Kill(Process leader, int leaderId)
    {
        ProcessTree.KillProcessOnly(leader);

        // ESRCH (no member left, or setsid had not run yet and the leader died before it) needs no handling.
        _ = UnixLibc.Send(-leaderId, UnixLibc.Kill);
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

    private static string? FindInPath(string fileName) =>
        FindInPath(fileName, Environment.GetEnvironmentVariable("PATH"));

    /// <summary>
    /// The first executable file named <paramref name="fileName"/> in the directories of <paramref name="path"/>, as
    /// <see cref="Process.Start()"/> searches <c>PATH</c>; a file without the execute permission is passed over.
    /// </summary>
    internal static string? FindInPath(string fileName, string? path)
    {
        if (path is null || path.Length == 0)
        {
            return null;
        }

        var rest = path.AsSpan();
        while (rest.Length > 0)
        {
            var end = rest.IndexOf(':');
            var directory = end < 0 ? rest : rest.Slice(0, end);
            rest = end < 0 ? ReadOnlySpan<char>.Empty : rest.Slice(end + 1);
            if (directory.Length > 0
                && ExistingIn(directory.ToString(), fileName) is { } found
                && UnixLibc.IsExecutable(found))
            {
                return found;
            }
        }

        return null;
    }

    // Whether execve will run the file itself: an ELF image, or a script whose #! line names an executable file.
    private static bool ExecAccepts(string program)
    {
        if (!UnixLibc.IsExecutable(program) || ReadHeader(program) is not { } header)
        {
            return false;
        }

        if (header.Length >= 4 && header[0] == 0x7F && header[1] == 'E' && header[2] == 'L' && header[3] == 'F')
        {
            return true;
        }

        return ShebangInterpreter(header) is { } interpreter
            && File.Exists(interpreter)
            && UnixLibc.IsExecutable(interpreter);
    }

    // The interpreter path of a "#!<spaces><path>[ <argument>]" line, or null.
    private static string? ShebangInterpreter(byte[] header)
    {
        if (header.Length < 2 || header[0] != '#' || header[1] != '!')
        {
            return null;
        }

        var start = 2;
        while (start < header.Length && header[start] is Space or Tab)
        {
            start++;
        }

        var end = start;
        while (end < header.Length && header[end] is not (Space or Tab or LineFeed or CarriageReturn))
        {
            end++;
        }

        return end > start ? Encoding.UTF8.GetString(header, start, end - start) : null;
    }

    private static byte[]? ReadHeader(string program)
    {
        try
        {
            using var stream = new FileStream(program, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1);
            var buffer = new byte[256];
            var length = stream.Read(buffer, 0, buffer.Length);
            Array.Resize(ref buffer, length);
            return buffer;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable: leave it to Process.Start, which reports it as it always did.
            return null;
        }
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
