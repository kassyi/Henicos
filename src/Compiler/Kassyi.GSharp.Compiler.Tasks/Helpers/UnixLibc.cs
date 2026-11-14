using System.Runtime.InteropServices;
using System.Text;

namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>The libc calls of the process helpers on Linux and macOS: signals and the execute permission.</summary>
internal static class UnixLibc
{
    /// <summary><c>SIGKILL</c>, the same number on Linux and macOS.</summary>
    public const int Kill = 9;

    // access(2) mode X_OK.
    private const int ExecuteAccess = 1;

    /// <summary><c>SIGSTOP</c>: 17 on macOS, 19 on Linux.</summary>
    public static readonly int Stop = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 17 : 19;

    /// <summary>
    /// Sends <paramref name="signal"/>; a negative <paramref name="processId"/> names a process group.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the signal was sent; <see langword="false"/> when no such process exists.
    /// </returns>
    public static bool Send(int processId, int signal) => SendSignal(processId, signal) == 0;

    /// <summary>Whether the current user may execute <paramref name="path"/>, as <c>exec</c> checks it.</summary>
    public static bool IsExecutable(string path)
    {
        // The path goes to libc as NUL-terminated UTF-8, the encoding of file names on Linux and macOS.
        var bytes = new byte[Encoding.UTF8.GetByteCount(path) + 1];
        Encoding.UTF8.GetBytes(path, 0, path.Length, bytes, 0);
        return Access(bytes, ExecuteAccess) == 0;
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SendSignal(int processId, int signal);

    [DllImport("libc", EntryPoint = "access", SetLastError = true)]
    private static extern int Access(byte[] path, int mode);
}
