using System.Globalization;

namespace Kassyi.GSharp.Runtime;

// The OS thread limits of Linux that a process can read (cgroup pids, RLIMIT_NPROC, kernel.threads-max,
// kernel.pid_max, vm.max_map_count) and the available memory of /proc/meminfo, for CooperativeStackBudget
internal static class LinuxThreadLimits
{
    private const int CooperativeVmasPerManagedThread = 4;
    private const string TestThreadLimitEnvVar = "GSHARP_TEST_STACK_THREAD_LIMIT";

    internal static bool TryReadMemAvailableBytes(out ulong availableBytes)
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

    internal static void Check(ulong threadCount, string kernelName)
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

        if (CooperativeStackBudget.TryReadEnvPositiveUInt64(TestThreadLimitEnvVar, out var overrideThreads))
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
}
