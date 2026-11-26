using System.Globalization;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime;

// The limit of a cooperative launch on the managed stack model: every GPU thread of the grid keeps an OS thread alive
// until the launch ends, so the launch is refused before any thread starts when the threads would not fit
internal static partial class CooperativeStackBudget
{
    private const long CooperativeStackBytesPerThread = 64L * 1024L;
    private const string TestMemoryLimitEnvVar = "GSHARP_TEST_STACK_MEMORY_LIMIT_BYTES";

    /// <summary>
    /// Checks whether a cooperative launch on the managed stack model, which keeps an OS thread for every GPU thread
    /// of the grid alive at once, fits within half of the available memory and, on Linux, within 80% of the readable
    /// OS thread limits before any thread is started.
    /// </summary>
    /// <param name="totalBlocks">The total number of blocks in the grid.</param>
    /// <param name="threadsPerBlock">The number of threads in each block.</param>
    /// <param name="kernelName">The kernel as <c>Type.Method</c>, for the exception message.</param>
    /// <exception cref="InsufficientMemoryException">
    /// The estimated stack memory exceeds half of the available memory, or on Linux the thread count plus existing
    /// threads exceeds 80% of a readable OS thread limit.
    /// </exception>
    internal static void Check(long totalBlocks, long threadsPerBlock, string kernelName)
    {
        var threadCount = checked((ulong)totalBlocks * (ulong)threadsPerBlock);
        var needBytes128 = (UInt128)threadCount * (ulong)CooperativeStackBytesPerThread;
        if (TryReadAvailableCooperativeMemoryBytes(out var availableBytes) && needBytes128 > availableBytes / 2UL)
        {
            var needMb = Math.Max(1UL, (ulong)((needBytes128 + (1024UL * 1024UL - 1UL)) / (1024UL * 1024UL)));
            var availMb = availableBytes / (1024UL * 1024UL);
            throw new InsufficientMemoryException(
                $"G#: the cooperative launch of {kernelName} keeps all {threadCount} GPU threads of its grid alive " +
                $"at once on the managed CPU path, one OS thread each; they need about {needMb} MB, but only " +
                $"{availMb} MB are available (half of the free memory is the limit). The GPU runs the launch.");
        }

        if (OperatingSystem.IsLinux())
        {
            LinuxThreadLimits.Check(threadCount, kernelName);
        }
    }

    private static bool TryReadAvailableCooperativeMemoryBytes(out ulong availableBytes)
    {
        if (TryReadEnvPositiveUInt64(TestMemoryLimitEnvVar, out availableBytes))
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (NativeMethods.GlobalMemoryStatusEx(ref status))
            {
                availableBytes = status.AvailablePageFile;
                return true;
            }

            availableBytes = 0;
            return false;
        }

        if (OperatingSystem.IsLinux() && LinuxThreadLimits.TryReadMemAvailableBytes(out availableBytes))
        {
            return true;
        }

        availableBytes = 0;
        return false;
    }

    internal static bool TryReadEnvPositiveUInt64(string name, out ulong value)
    {
        if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } raw &&
            ulong.TryParse(raw.AsSpan().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
            value > 0UL)
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhysical;
        internal ulong AvailablePhysical;
        internal ulong TotalPageFile;
        internal ulong AvailablePageFile;
        internal ulong TotalVirtual;
        internal ulong AvailableVirtual;
        internal ulong AvailableExtendedVirtual;
    }
}
