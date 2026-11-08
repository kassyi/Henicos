#nullable enable
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Reads the memory high-water mark of the current process, native allocations included, for the test memory budget.
/// </summary>
/// <remarks>
/// The metric differs per operating system because no portable API gives the peak private memory:
/// <list type="bullet">
///   <item><description>Windows: the peak commit charge of the process (<c>PeakPagefileUsage</c> of
///   <c>GetProcessMemoryInfo</c>), which counts every private allocation (GC heap, native heap, privately loaded
///   metadata) whether or not it was touched.</description></item>
///   <item><description>Linux: the peak resident set size (<c>VmHWM</c> of <c>/proc/self/status</c>), which counts the
///   pages that were touched, including mapped files such as loaded assemblies.</description></item>
///   <item><description>Other systems: <see cref="Process.PeakWorkingSet64"/>.</description></item>
/// </list>
/// Both counters are maintained by the kernel, so a peak reached between two samples is still seen by the next one.
/// </remarks>
public static class ProcessMemory
{
    private const string LinuxStatusPath = "/proc/self/status";
    private const string LinuxPeakField = "VmHWM:";

    /// <summary>
    /// Gets the name of the metric that <see cref="ReadPeakBytes"/> returns on this operating system.
    /// </summary>
    public static string PeakMetricName { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "peak private memory (PeakPagefileUsage)"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "peak resident memory (VmHWM)"
        : "peak working set";

    /// <summary>
    /// Reads the memory high-water mark of the current process.
    /// </summary>
    /// <returns>The peak in bytes.</returns>
    /// <exception cref="IOException"><c>/proc/self/status</c> could not be read or has no <c>VmHWM</c> line (Linux).</exception>
    public static long ReadPeakBytes()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ReadWindowsPeakCommit();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return ReadLinuxPeakResident(File.ReadAllText(LinuxStatusPath));
        }

        using var process = Process.GetCurrentProcess();
        return process.PeakWorkingSet64;
    }

    /// <summary>
    /// Reads the bytes allocated on the managed heap and not yet collected.
    /// </summary>
    /// <returns>The GC heap size in bytes.</returns>
    /// <remarks><see cref="GC.GetGCMemoryInfo()"/> describes the last collection only, so it reads 0 before the first one.</remarks>
    public static long ReadGcHeapBytes() => GC.GetTotalMemory(forceFullCollection: false);

    /// <summary>
    /// Parses the <c>VmHWM</c> line of the text of <c>/proc/self/status</c>.
    /// </summary>
    /// <param name="status">The content of <c>/proc/self/status</c>.</param>
    /// <returns>The peak resident set size in bytes.</returns>
    /// <exception cref="IOException">The text has no valid <c>VmHWM</c> line.</exception>
    public static long ReadLinuxPeakResident(string status)
    {
        foreach (ReadOnlySpan<char> line in status.AsSpan().EnumerateLines())
        {
            if (!line.StartsWith(LinuxPeakField, StringComparison.Ordinal))
            {
                continue;
            }

            // VmHWM:\t  123456 kB
            ReadOnlySpan<char> value = line[LinuxPeakField.Length..].Trim();
            int space = value.IndexOf(' ');
            ReadOnlySpan<char> number = space < 0 ? value : value[..space];
            if (long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out long kibibytes))
            {
                return kibibytes * 1024;
            }

            break;
        }

        throw new IOException($"{LinuxStatusPath} has no valid {LinuxPeakField} line.");
    }

    private static long ReadWindowsPeakCommit()
    {
        var counters = new ProcessMemoryCounters { Cb = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
        if (GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Cb))
        {
            return (long)counters.PeakPagefileUsage.ToUInt64();
        }

        using var process = Process.GetCurrentProcess();
        return process.PeakPagedMemorySize64;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCounters counters, uint size);

    // PROCESS_MEMORY_COUNTERS of psapi.h
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
    }
}
