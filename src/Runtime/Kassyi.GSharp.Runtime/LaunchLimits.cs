using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime;

/// <summary>
/// Checks the dimensions of a kernel launch against the limits that every GPU enforces. The generated launch
/// methods call it before any work, on CUDA and on both CPU paths, so that every path rejects the same launches in
/// the same way.
/// </summary>
/// <remarks>
/// The CPU paths accept every launch that the GPU accepts and reject what any of the declared target GPUs rejects:
/// <see cref="CheckLaunch"/> checks the dimension limits shared by every GPU, and
/// <see cref="CheckTargetSharedMemory(uint, int, int, string, string)"/> checks the per-block shared memory limit of
/// the target GPUs from <c>GSharpTargetArchitectures</c> (on CUDA, <see cref="Aot.DynamicSharedMemoryOptIn"/> checks
/// the real device). The run time or the memory of the machine never adds a limit. The managed path still keeps a
/// context for every thread of the grid in one array (<see cref="GetManagedContextCount"/>), a limit of that
/// implementation, not of the launch.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static partial class LaunchLimits
{
    /// <summary>The largest <c>gridDimX</c> of every GPU (2^31 - 1).</summary>
    public const uint MaxGridDimX = int.MaxValue;

    /// <summary>The largest <c>gridDimY</c> and <c>gridDimZ</c> of every GPU.</summary>
    public const uint MaxGridDimYZ = 65535;

    /// <summary>The largest <c>blockDimX</c> and <c>blockDimY</c> of every GPU.</summary>
    public const uint MaxBlockDimXY = 1024;

    /// <summary>The largest <c>blockDimZ</c> of every GPU.</summary>
    public const uint MaxBlockDimZ = 64;

    /// <summary>The most threads in one block on every GPU.</summary>
    public const uint MaxThreadsPerBlock = 1024;

    private const long CooperativeStackBytesPerThread = 64L * 1024L;
    private const int CooperativeVmasPerManagedThread = 4;
    private const string TestMemoryLimitEnvVar = "GSHARP_TEST_STACK_MEMORY_LIMIT_BYTES";
    private const string TestThreadLimitEnvVar = "GSHARP_TEST_STACK_THREAD_LIMIT";

    /// <summary>
    /// Throws when a launch dimension is 0 or above the limit of every GPU, and returns the number of blocks of the
    /// grid.
    /// </summary>
    /// <param name="gridDimX">The number of blocks in x.</param>
    /// <param name="gridDimY">The number of blocks in y.</param>
    /// <param name="gridDimZ">The number of blocks in z.</param>
    /// <param name="blockDimX">The number of threads per block in x.</param>
    /// <param name="blockDimY">The number of threads per block in y.</param>
    /// <param name="blockDimZ">The number of threads per block in z.</param>
    /// <param name="kernelName">The kernel as <c>Type.Method</c>, for the exception message.</param>
    /// <returns>
    /// <c>gridDimX * gridDimY * gridDimZ</c>, counted in 64 bits: within the limits it is below 2^63, so it never
    /// wraps.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is 0 or above its limit, or the block has more than <see cref="MaxThreadsPerBlock"/> threads; the
    /// parameter name is the dimension.
    /// </exception>
    public static long CheckLaunch(uint gridDimX, uint gridDimY, uint gridDimZ, uint blockDimX, uint blockDimY,
        uint blockDimZ, string kernelName)
    {
        ThrowIfOutOfRange(gridDimX, MaxGridDimX, "gridDimX", "a grid can have at most 2147483647 blocks in x",
            kernelName);
        ThrowIfOutOfRange(gridDimY, MaxGridDimYZ, "gridDimY", "a grid can have at most 65535 blocks in y",
            kernelName);
        ThrowIfOutOfRange(gridDimZ, MaxGridDimYZ, "gridDimZ", "a grid can have at most 65535 blocks in z",
            kernelName);
        ThrowIfOutOfRange(blockDimX, MaxBlockDimXY, "blockDimX", "a block can have at most 1024 threads in x",
            kernelName);
        ThrowIfOutOfRange(blockDimY, MaxBlockDimXY, "blockDimY", "a block can have at most 1024 threads in y",
            kernelName);
        ThrowIfOutOfRange(blockDimZ, MaxBlockDimZ, "blockDimZ", "a block can have at most 64 threads in z",
            kernelName);

        // Each factor is at most 1024 here, so the product cannot wrap
        var threadsPerBlock = blockDimX * blockDimY * blockDimZ;
        if (threadsPerBlock > MaxThreadsPerBlock)
        {
            throw new ArgumentOutOfRangeException(nameof(blockDimX),
                $"G#: a block of {blockDimX} x {blockDimY} x {blockDimZ} threads exceeds the " +
                $"{MaxThreadsPerBlock} threads per block of every GPU ({kernelName}).");
        }

        return (long)gridDimX * gridDimY * gridDimZ;
    }

    /// <summary>
    /// Checks the shared memory of a launch against the smallest per-block shared memory limit (with opt-in) of the
    /// target GPUs from <c>GSharpTargetArchitectures</c>. For generated code only.
    /// </summary>
    /// <param name="sharedMemBytes">The dynamic shared memory requested by the launch, in bytes.</param>
    /// <param name="staticSharedBytes">The static shared memory of the kernel, in bytes.</param>
    /// <param name="maxSharedMemoryPerBlock">
    /// The per-block shared memory limit of the strictest target GPU, in bytes.
    /// </param>
    /// <param name="architecture">The compute capability of that target GPU (such as <c>sm_86</c>).</param>
    /// <param name="kernelName">The kernel as <c>Type.Method</c>, for the exception message.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sharedMemBytes"/> plus <paramref name="staticSharedBytes"/> exceeds
    /// <paramref name="maxSharedMemoryPerBlock"/>.
    /// </exception>
    public static void CheckTargetSharedMemory(uint sharedMemBytes, int staticSharedBytes, int maxSharedMemoryPerBlock,
        string architecture, string kernelName) =>
        CheckTargetSharedMemory(sharedMemBytes, staticSharedBytes, maxSharedMemoryPerBlock, architecture, kernelName,
            oversizedSharedMemoryPerBlock: 0);

    /// <summary>
    /// Checks that the dynamic and static shared memory of a launch does not exceed the limit of the strictest target
    /// GPU of <c>GSharpTargetArchitectures</c>, with a hint when an oversized shared memory configuration is available.
    /// </summary>
    /// <param name="sharedMemBytes">The dynamic shared memory per block that the launch requests.</param>
    /// <param name="staticSharedBytes">The static shared memory per block of the kernel, in bytes.</param>
    /// <param name="maxSharedMemoryPerBlock">
    /// The per-block shared memory limit of the strictest target GPU, in bytes.
    /// </param>
    /// <param name="architecture">The compute capability of that target GPU (such as <c>sm_86</c>).</param>
    /// <param name="kernelName">The kernel as <c>Type.Method</c>, for the exception message.</param>
    /// <param name="oversizedSharedMemoryPerBlock">
    /// The oversized shared memory limit of the target GPU, in bytes, when the kernel could use it by enabling
    /// <c>[GpuKernel(AllowOversizedSharedMemory = true)]</c>, or 0 when no hint is shown.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sharedMemBytes"/> plus <paramref name="staticSharedBytes"/> exceeds
    /// <paramref name="maxSharedMemoryPerBlock"/>.
    /// </exception>
    public static void CheckTargetSharedMemory(uint sharedMemBytes, int staticSharedBytes, int maxSharedMemoryPerBlock,
        string architecture, string kernelName, int oversizedSharedMemoryPerBlock)
    {
        if ((long)sharedMemBytes + staticSharedBytes > maxSharedMemoryPerBlock)
        {
            var hint = oversizedSharedMemoryPerBlock > 0
                ? $"; this GPU holds up to {oversizedSharedMemoryPerBlock} bytes for a kernel with " +
                  "[GpuKernel(AllowOversizedSharedMemory = true)]"
                : "";
            throw new ArgumentOutOfRangeException(nameof(sharedMemBytes),
                $"G#: sharedMemBytes {sharedMemBytes} plus {staticSharedBytes} bytes of static shared memory exceeds " +
                $"the {maxSharedMemoryPerBlock} bytes of shared memory per block of the target GPU {architecture} " +
                $"(GSharpTargetArchitectures); the GPU rejects the launch with CUDA_ERROR_INVALID_VALUE{hint} " +
                $"({kernelName}).");
        }
    }

    /// <summary>
    /// The number of thread contexts of a launch on the managed CPU path, which keeps one for every thread of the
    /// grid in one array. For generated code only.
    /// </summary>
    /// <param name="totalBlocks">The number of blocks, from <see cref="CheckLaunch"/>.</param>
    /// <param name="blockDimX">The number of threads per block in x.</param>
    /// <param name="blockDimY">The number of threads per block in y.</param>
    /// <param name="blockDimZ">The number of threads per block in z.</param>
    /// <param name="kernelName">The kernel as <c>Type.Method</c>, for the exception message.</param>
    /// <returns>The number of threads of the launch.</returns>
    /// <exception cref="NotSupportedException">
    /// The launch has more threads than one array holds (<see cref="Array.MaxLength"/>), a limit of the managed
    /// path only.
    /// </exception>
    public static int GetManagedContextCount(long totalBlocks, uint blockDimX, uint blockDimY, uint blockDimZ,
        string kernelName)
    {
        // At most 1024 threads per block after CheckLaunch, so the comparison below never wraps
        long threadsPerBlock = (long)blockDimX * blockDimY * blockDimZ;
        if (totalBlocks > Array.MaxLength / threadsPerBlock)
        {
            throw new NotSupportedException(
                $"G#: the launch has {totalBlocks} blocks of {threadsPerBlock} threads, more than the " +
                $"{Array.MaxLength} threads that the managed CPU path can run in one launch ({kernelName}). " +
                "This is a limit of the managed path, which keeps a context for every thread of the grid in one " +
                "array; the native CPU path and the GPU run the launch. " +
                "See https://github.com/kassyi/Henicos/issues/1443 .");
        }

        return (int)(totalBlocks * threadsPerBlock);
    }

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
    public static void CheckManagedCooperativeStackMemory(long totalBlocks, long threadsPerBlock, string kernelName)
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
            CheckLinuxCooperativeThreadLimits(threadCount, kernelName);
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

        if (OperatingSystem.IsLinux() && TryReadLinuxMemAvailableBytes(out availableBytes))
        {
            return true;
        }

        availableBytes = 0;
        return false;
    }

    private static bool TryReadEnvPositiveUInt64(string name, out ulong value)
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

    private static bool TryReadLinuxMemAvailableBytes(out ulong availableBytes)
    {
        if (TryReadProcFile("/proc/meminfo") is { } text)
        {
            foreach (var rawLine in text.AsSpan().EnumerateLines())
            {
                var line = rawLine.Trim();
                const string prefix = "MemAvailable:";
                if (!line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var rest = line[prefix.Length..].TrimStart();
                var space = rest.IndexOf(' ');
                var token = space < 0 ? rest : rest[..space];
                if (ulong.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kib))
                {
                    availableBytes = kib > ulong.MaxValue / 1024UL ? ulong.MaxValue : kib * 1024UL;
                    return true;
                }

                break;
            }
        }

        availableBytes = 0;
        return false;
    }

    private static void CheckLinuxCooperativeThreadLimits(ulong threadCount, string kernelName)
    {
        var processThreads = ReadProcessThreadCount();
        var hasLimit = false;
        var minMayStart = ulong.MaxValue;
        var limitingName = string.Empty;

        void ConsiderLimit(ulong cap80, ulong currentUsed, string name)
        {
            var effectiveUsed = Math.Max(currentUsed, processThreads);
            var mayStart = cap80 > effectiveUsed ? cap80 - effectiveUsed : 0UL;
            if (!hasLimit || mayStart < minMayStart)
            {
                hasLimit = true;
                minMayStart = mayStart;
                limitingName = name;
            }
        }

        if (TryReadEnvPositiveUInt64(TestThreadLimitEnvVar, out var overrideThreads))
        {
            ConsiderLimit(EightiethPercent(overrideThreads), processThreads, TestThreadLimitEnvVar);
        }
        else
        {
            ConsiderCgroupPidsLimits(processThreads, ConsiderLimit);
            ConsiderRlimitNproc(processThreads, ConsiderLimit);

            var nrThreads = ReadLoadAvgThreads() ?? processThreads;
            if (TryReadSingleUInt64File("/proc/sys/kernel/threads-max", out var threadsMax))
            {
                ConsiderLimit(EightiethPercent(threadsMax), nrThreads, "kernel.threads-max");
            }

            if (TryReadSingleUInt64File("/proc/sys/kernel/pid_max", out var pidMax))
            {
                ConsiderLimit(EightiethPercent(pidMax), nrThreads, "kernel.pid_max");
            }

            if (TryReadSingleUInt64File("/proc/sys/vm/max_map_count", out var maxMapCount))
            {
                var currentMaps = CountProcSelfMaps() ?? processThreads * (ulong)CooperativeVmasPerManagedThread;
                var vmaCap80 = EightiethPercent(maxMapCount);
                var mayStartVma = vmaCap80 > currentMaps
                    ? (vmaCap80 - currentMaps) / (ulong)CooperativeVmasPerManagedThread
                    : 0UL;
                if (!hasLimit || mayStartVma < minMayStart)
                {
                    hasLimit = true;
                    minMayStart = mayStartVma;
                    limitingName = "vm.max_map_count";
                }
            }
        }

        if (hasLimit && threadCount > minMayStart)
        {
            throw new InsufficientMemoryException(
                $"G#: the cooperative launch of {kernelName} needs {threadCount} OS threads on the managed CPU " +
                $"path, one per GPU thread, but this process may start only about {minMayStart} ({limitingName}). " +
                "The GPU runs the launch.");
        }
    }

    private static ulong EightiethPercent(ulong limit) =>
        (limit / 5UL) * 4UL + ((limit % 5UL) * 4UL) / 5UL;

    private static void ConsiderCgroupPidsLimits(ulong processThreads, Action<ulong, ulong, string> considerLimit)
    {
        if (TryReadProcFile("/proc/self/cgroup") is not { } cgroupText)
        {
            return;
        }

        foreach (var rawLine in cgroupText.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            if (line.IsEmpty)
            {
                continue;
            }

            if (line.StartsWith("0::", StringComparison.Ordinal))
            {
                WalkCgroupHierarchy("/sys/fs/cgroup", line["0::".Length..].Trim(), processThreads, considerLimit);
                continue;
            }

            var firstColon = line.IndexOf(':');
            if (firstColon < 0)
            {
                continue;
            }

            var afterFirst = line[(firstColon + 1)..];
            var secondColon = afterFirst.IndexOf(':');
            if (secondColon < 0)
            {
                continue;
            }

            var controllers = afterFirst[..secondColon];
            var relPath = afterFirst[(secondColon + 1)..].Trim();
            var hasPidsController = false;
            foreach (var range in controllers.Split(','))
            {
                if (controllers[range].Equals("pids", StringComparison.Ordinal))
                {
                    hasPidsController = true;
                    break;
                }
            }

            if (hasPidsController && Directory.Exists("/sys/fs/cgroup/pids"))
            {
                WalkCgroupHierarchy("/sys/fs/cgroup/pids", relPath, processThreads, considerLimit);
            }
        }
    }

    private static void WalkCgroupHierarchy(string rootDir, ReadOnlySpan<char> relPath, ulong processThreads,
        Action<ulong, ulong, string> considerLimit)
    {
        if (!Directory.Exists(rootDir))
        {
            return;
        }

        var trimmed = relPath.TrimStart('/');
        foreach (var range in trimmed.Split('/'))
        {
            if (trimmed[range].Equals("..", StringComparison.Ordinal))
            {
                return;
            }
        }

        var currentDir = trimmed.IsEmpty ? rootDir : Path.Combine(rootDir, new string(trimmed));
        while (!string.IsNullOrEmpty(currentDir) &&
               currentDir.StartsWith(rootDir, StringComparison.Ordinal))
        {
            if (Directory.Exists(currentDir))
            {
                var maxPath = Path.Combine(currentDir, "pids.max");
                if (TryReadSingleUInt64File(maxPath, out var maxPids))
                {
                    var currentPath = Path.Combine(currentDir, "pids.current");
                    var currentPids = TryReadSingleUInt64File(currentPath, out var current)
                        ? current
                        : processThreads;
                    considerLimit(EightiethPercent(maxPids), currentPids, "cgroup pids.max");
                }
            }

            if (currentDir.Equals(rootDir, StringComparison.Ordinal))
            {
                break;
            }

            currentDir = Path.GetDirectoryName(currentDir);
        }
    }

    private static void ConsiderRlimitNproc(ulong processThreads, Action<ulong, ulong, string> considerLimit)
    {
        if (TryReadProcFile("/proc/self/limits") is not { } limitsText)
        {
            return;
        }

        const string label = "Max processes";
        foreach (var rawLine in limitsText.AsSpan().EnumerateLines())
        {
            var line = rawLine.TrimStart();
            if (!line.StartsWith(label, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line[label.Length..].TrimStart();
            var end = rest.IndexOfAny(' ', '\t');
            var softToken = end < 0 ? rest : rest[..end];
            if (!softToken.Equals("unlimited", StringComparison.OrdinalIgnoreCase) &&
                ulong.TryParse(softToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var softLimit))
            {
                var uidThreads = CountUidThreads() ?? processThreads;
                considerLimit(EightiethPercent(softLimit), uidThreads, "RLIMIT_NPROC");
            }

            break;
        }
    }

    private static ulong ReadProcessThreadCount()
    {
        if (TryReadProcFile("/proc/self/status") is { } statusText &&
            TryParseStatusFields(statusText.AsSpan(), out _, out var threads))
        {
            return threads;
        }

        return 1UL;
    }

    private static ulong? CountUidThreads()
    {
        if (TryReadProcFile("/proc/self/status") is not { } selfStatus ||
            !TryParseStatusFields(selfStatus.AsSpan(), out var realUid, out _) ||
            realUid is not { } targetUid)
        {
            return null;
        }

        ulong total = 0;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                var name = Path.GetFileName(dir.AsSpan());
                if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }

                if (TryReadProcFile(Path.Combine(dir, "status")) is { } status &&
                    TryParseStatusFields(status.AsSpan(), out var uid, out var threads) &&
                    uid == targetUid)
                {
                    total += threads;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // /proc enumeration can race with process exit or hit restricted entries; fall back cleanly.
            return null;
        }

        return total > 0 ? total : null;
    }

    private static bool TryParseStatusFields(ReadOnlySpan<char> text, out ulong? realUid, out ulong threads)
    {
        realUid = null;
        threads = 0;
        var foundThreads = false;
        foreach (var rawLine in text.EnumerateLines())
        {
            var line = rawLine.Trim();
            if (line.StartsWith("Uid:", StringComparison.Ordinal))
            {
                var rest = line["Uid:".Length..].TrimStart();
                var end = rest.IndexOfAny(' ', '\t');
                var first = end < 0 ? rest : rest[..end];
                if (ulong.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedUid))
                {
                    realUid = parsedUid;
                }
            }
            else if (line.StartsWith("Threads:", StringComparison.Ordinal))
            {
                var rest = line["Threads:".Length..].Trim();
                if (ulong.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedThreads))
                {
                    threads = parsedThreads;
                    foundThreads = true;
                }
            }

            if (realUid.HasValue && foundThreads)
            {
                break;
            }
        }

        return foundThreads;
    }

    private static ulong? ReadLoadAvgThreads()
    {
        if (TryReadProcFile("/proc/loadavg") is not { } text)
        {
            return null;
        }

        var span = text.AsSpan().Trim();
        var fieldIndex = 0;
        foreach (var range in span.Split(' '))
        {
            var token = span[range];
            if (token.IsEmpty)
            {
                continue;
            }

            if (fieldIndex == 3)
            {
                var slash = token.IndexOf('/');
                if (slash >= 0 &&
                    ulong.TryParse(token[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var nrThreads))
                {
                    return nrThreads;
                }

                return null;
            }

            fieldIndex++;
        }

        return null;
    }

    private static ulong? CountProcSelfMaps()
    {
        try
        {
            using var stream = new FileStream("/proc/self/maps", FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> buffer = stackalloc byte[4096];
            ulong lines = 0;
            long totalBytes = 0;
            byte lastByte = 0;
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                totalBytes += read;
                lastByte = buffer[read - 1];
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] == (byte)'\n')
                    {
                        lines++;
                    }
                }
            }

            if (totalBytes > 0 && lastByte != (byte)'\n')
            {
                lines++;
            }

            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // /proc/self/maps may be unavailable or restricted in minimal environments; fall back cleanly.
            return null;
        }
    }

    private static bool TryReadSingleUInt64File(string path, out ulong value)
    {
        if (TryReadProcFile(path) is { } text &&
            ulong.TryParse(text.AsSpan().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static string? TryReadProcFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // /proc or cgroup entries may vanish (process exit) or be unreadable; ignore cleanly.
            return null;
        }
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

    private static void ThrowIfOutOfRange(uint value, uint max, string name, string limit, string kernelName)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(name,
                $"G#: {name} is 0; every launch dimension must be at least 1 ({kernelName}).");
        }

        if (value > max)
        {
            throw new ArgumentOutOfRangeException(name,
                $"G#: {name} is {value}; {limit} on every GPU ({kernelName}).");
        }
    }
}
