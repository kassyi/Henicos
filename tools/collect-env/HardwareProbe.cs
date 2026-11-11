using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using static Collector;
using static Privacy;
using static ProcessProbe;

/// <summary>Reads the OS, CPU, memory and motherboard.</summary>
internal static class HardwareProbe
{
    private static string? RegistryString(string subKey, string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(subKey);
            object? value = key?.GetValue(name);
            return value switch
            {
                string s => s.Trim(),
                string[] { Length: > 0 } many => many[0].Trim(),
                int n => n.ToString(CultureInfo.InvariantCulture),
                _ => null,
            };
        }
        catch (Exception ex) when (
            ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Warn($@"cannot read registry value HKLM\{subKey}\{name}", ex);
            return null;
        }
    }

    internal static string WindowsOs()
    {
        const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        string name = RegistryString(key, "ProductName") ?? "Windows";
        string build = RegistryString(key, "CurrentBuild")
            ?? Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
        // The registry still says "Windows 10" on Windows 11.
        if (int.TryParse(build, NumberStyles.None, CultureInfo.InvariantCulture, out int buildNumber)
            && buildNumber >= 22000
            && name.StartsWith("Windows 10", StringComparison.Ordinal))
        {
            name = "Windows 11" + name.Substring("Windows 10".Length);
        }

        string display = RegistryString(key, "DisplayVersion") ?? RegistryString(key, "ReleaseId") ?? string.Empty;
        string ubr = RegistryString(key, "UBR") is { } u ? "." + u : string.Empty;
        string label = display.Length > 0 ? $"{name} {display}" : name;
        Version version = Environment.OSVersion.Version;
        return $"{label} ({version.Major}.{version.Minor}.{build}{ubr}), {Architecture()}";
    }

    private static string Architecture() => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    internal static string LinuxOs()
    {
        string name = "Linux";
        foreach (string line in Lines(ReadFile("/etc/os-release")))
        {
            if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
            {
                name = line.Substring("PRETTY_NAME=".Length).Trim('"');
            }
        }

        string kernel = Run("uname", "-r") ?? ReadFile("/proc/sys/kernel/osrelease")?.Trim() ?? NotFound;
        string wsl = string.Empty;
        string? procVersion = ReadFile("/proc/version");
        if (IsSet("WSL_DISTRO_NAME")
            || (procVersion is not null && procVersion.Contains("microsoft", StringComparison.OrdinalIgnoreCase)))
        {
            wsl = procVersion is not null && procVersion.Contains("WSL2", StringComparison.OrdinalIgnoreCase)
                ? ", WSL2"
                : ", WSL";
        }

        return Clean($"{name}, kernel {kernel}{wsl}, {Architecture()}");
    }

    internal static string WindowsCpu()
    {
        string? name = RegistryString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
        return name is null ? NotFound : CollapseSpaces(name);
    }

    internal static string LinuxCpu()
    {
        foreach (string line in Lines(ReadFile("/proc/cpuinfo")))
        {
            if (line.StartsWith("model name", StringComparison.Ordinal))
            {
                int colon = line.IndexOf(':');
                if (colon >= 0)
                {
                    return CollapseSpaces(line.Substring(colon + 1));
                }
            }
        }

        return NotFound;
    }

    private static string CollapseSpaces(string text) =>
        string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    internal static string Memory(bool windows)
    {
        long bytes = 0;
        if (windows)
        {
            bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
        else
        {
            foreach (string line in Lines(ReadFile("/proc/meminfo")))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    string number = line.Substring("MemTotal:".Length)
                        .Replace("kB", string.Empty, StringComparison.Ordinal)
                        .Trim();
                    if (long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out long kb))
                    {
                        bytes = kb * 1024;
                    }
                }
            }
        }

        return bytes <= 0
            ? NotFound
            : $"{Math.Round(bytes / (1024.0 * 1024 * 1024)).ToString("0", CultureInfo.InvariantCulture)} GB";
    }

    internal static string WindowsBoard()
    {
        const string key = @"HARDWARE\DESCRIPTION\System\BIOS";
        return BoardText(
            RegistryString(key, "BaseBoardManufacturer"),
            RegistryString(key, "BaseBoardProduct"),
            RegistryString(key, "BIOSVersion"));
    }

    internal static string LinuxBoard()
    {
        const string dir = "/sys/class/dmi/id/";
        return BoardText(
            ReadFile(dir + "board_vendor")?.Trim(),
            ReadFile(dir + "board_name")?.Trim(),
            ReadFile(dir + "bios_version")?.Trim());
    }

    private static string BoardText(string? vendor, string? product, string? bios)
    {
        string board = $"{vendor} {product}".Trim();
        if (board.Length == 0 && bios is null)
        {
            return NotFound;
        }

        return Clean($"{(board.Length == 0 ? NotFound : board)}, BIOS {bios ?? NotFound}");
    }
}
