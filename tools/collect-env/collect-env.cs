#:property PublishAot=true
#:property InvariantGlobalization=true
#:property JsonSerializerIsReflectionEnabledByDefault=false
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property GenerateDocumentationFile=false

// collect-env: prints the hardware and software environment of a G# bug report as Markdown (default) or JSON.
//
// Usage:
//   dotnet run tools/collect-env/collect-env.cs [-- --output <file>] [--json] [--help]
//   collect-env-win-x64.exe | collect-env-linux-x64   (NativeAOT build, see below)
//
// Publish (NativeAOT cannot cross-compile; build each target on its own OS):
//   dotnet publish tools/collect-env/collect-env.cs -r win-x64      (Windows)
//   dotnet publish tools/collect-env/collect-env.cs -r linux-x64    (Linux / WSL; needs clang and zlib headers)
//
// Rules: no reflection (System.Text.Json source generator), no WMI / PowerShell (registry on Windows), no project
// references (it must run when the G# build or the .NET SDK is broken). A missing tool is reported as "not found".
// Privacy: no user name, host name, serial numbers or absolute paths under the user profile; CUDA_PATH is only
// reported as set or unset. The MSVC query repeats MsvcToolchain.ResolveVcvarsEnvironment (src/Compiler/
// Kassyi.GSharp.Compiler.Tasks/Toolchains/MsvcToolchain.cs); keep both in sync.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

string? outputPath = null;
bool json = false;
for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];
    if (arg is "--help" or "-h")
    {
        Console.Out.Write(Collector.HelpText);
        return 0;
    }

    if (arg == "--json")
    {
        json = true;
    }
    else if (arg == "--output")
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine("collect-env: --output needs a file path.");
            return 2;
        }

        outputPath = args[++i];
    }
    else if (arg.StartsWith("--output=", StringComparison.Ordinal))
    {
        outputPath = arg.Substring("--output=".Length);
    }
    else
    {
        Console.Error.WriteLine($"collect-env: unknown option '{arg}'. Use --help.");
        return 2;
    }
}

ReportModel model = Collector.Collect();
string text = json
    ? JsonSerializer.Serialize(model, ReportJsonContext.Default.ReportModel) + "\n"
    : Collector.ToMarkdown(model);

if (outputPath is null)
{
    Console.Out.Write(text);
    return 0;
}

try
{
    File.WriteAllText(outputPath, text, new UTF8Encoding(false));
    Console.Out.WriteLine($"collect-env: wrote {Collector.Clean(Path.GetFullPath(outputPath))}");
    return 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine($"collect-env: cannot write '{outputPath}': {Collector.Clean(ex.Message)}");
    return 1;
}

/// <summary>The collected report; the shape of the <c>--json</c> output.</summary>
public sealed class ReportModel
{
    public string Os { get; set; } = Collector.NotFound;
    public string Cpu { get; set; } = Collector.NotFound;
    public string Memory { get; set; } = Collector.NotFound;
    public string Motherboard { get; set; } = Collector.NotFound;
    public List<string> Gpus { get; set; } = [];
    public string CudaToolkit { get; set; } = Collector.NotFound;
    public string CudaPath { get; set; } = "unset";
    public string HostCpp { get; set; } = Collector.NotFound;
    public string DotnetSdk { get; set; } = Collector.NotFound;
    public string GlobalJson { get; set; } = "none";
    public List<string> DotnetSdks { get; set; } = [];
    public List<string> DotnetRuntimes { get; set; } = [];
    public string? GSharp { get; set; }
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReportModel))]
internal sealed partial class ReportJsonContext : JsonSerializerContext;

internal static class Collector
{
    public const string NotFound = "not found";
    private const int TimeoutMilliseconds = 8000;

    public const string HelpText =
        """
        collect-env: prints the environment of a G# bug report (OS, CPU, GPU, CUDA, host C++ compiler, .NET, checkout).

        Usage: collect-env [--output <file>] [--json] [--help]

          --output <file>   write the report to <file> instead of stdout
          --json            machine-readable JSON instead of Markdown
          --help            show this text

        The report leaves out the user name, host name, serial numbers and paths under the user profile (shown as ~).
        CUDA_PATH is reported as set or unset only. A missing tool is shown as "not found".

        """;

    private static readonly string s_userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static ReportModel Collect()
    {
        var model = new ReportModel();
        bool windows = OperatingSystem.IsWindows();
        model.Os = windows ? WindowsOs() : LinuxOs();
        model.Cpu = Clean($"{(windows ? WindowsCpu() : LinuxCpu())}, {Environment.ProcessorCount} logical cores");
        model.Memory = Memory(windows);
        model.Motherboard = windows ? WindowsBoard() : LinuxBoard();
        CollectGpus(model);
        model.CudaToolkit = CudaToolkit();
        model.CudaPath = IsSet("CUDA_PATH") ? "set" : "unset";
        model.HostCpp = windows ? Msvc() : LinuxCompilers();
        CollectDotnet(model);
        model.GSharp = GSharpCheckout();
        CollectEnvironmentVariables(model);
        return model;
    }

    public static string ToMarkdown(ReportModel m)
    {
        var sb = new StringBuilder();
        sb.Append("- OS: ").Append(m.Os).Append('\n');
        sb.Append("- CPU / RAM: ").Append(m.Cpu).Append(", ").Append(m.Memory).Append('\n');
        sb.Append("- Motherboard: ").Append(m.Motherboard).Append('\n');
        if (m.Gpus.Count == 0)
        {
            sb.Append("- GPU: ").Append(NotFound).Append('\n');
        }

        for (int i = 0; i < m.Gpus.Count; i++)
        {
            sb.Append("- GPU ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": ").Append(m.Gpus[i]).Append('\n');
        }

        sb.Append("- CUDA Toolkit: ").Append(m.CudaToolkit).Append(" (CUDA_PATH ").Append(m.CudaPath).Append(")\n");
        sb.Append("- Host C++: ").Append(m.HostCpp).Append('\n');
        sb.Append("- .NET: SDK ").Append(m.DotnetSdk).Append(" (global.json: ").Append(m.GlobalJson).Append(")\n");
        sb.Append("  - SDKs: ").Append(Join(m.DotnetSdks)).Append('\n');
        sb.Append("  - Runtimes: ").Append(Join(m.DotnetRuntimes)).Append('\n');
        if (m.GSharp is not null)
        {
            sb.Append("- G#: ").Append(m.GSharp).Append('\n');
        }

        sb.Append("- G# environment variables: ");
        bool first = true;
        foreach (KeyValuePair<string, string> pair in m.EnvironmentVariables)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(pair.Key).Append('=').Append(pair.Value);
        }

        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>Removes what must not reach a public issue: profile paths, user name and host name.</summary>
    public static string Clean(string value)
    {
        string result = value;
        if (s_userProfile.Length > 0)
        {
            result = result.Replace(s_userProfile, "~", StringComparison.OrdinalIgnoreCase);
            result = result.Replace(s_userProfile.Replace('\\', '/'), "~", StringComparison.OrdinalIgnoreCase);
        }

        // The longer name goes first: a host name often contains the user name ("runner" in "runnervm..."), and
        // replacing the user name first would leave the rest of the host name in the report.
        string user = Environment.UserName;
        string host = Environment.MachineName;
        if (host.Length >= user.Length)
        {
            result = ReplaceName(ReplaceName(result, host, "<host>"), user, "<user>");
        }
        else
        {
            result = ReplaceName(ReplaceName(result, user, "<user>"), host, "<host>");
        }

        return result;
    }

    // Names shorter than 3 characters are left alone: replacing them would corrupt unrelated words.
    private static string ReplaceName(string value, string name, string placeholder) =>
        name.Length >= 3 ? value.Replace(name, placeholder, StringComparison.OrdinalIgnoreCase) : value;

    private static string Join(List<string> items) => items.Count == 0 ? NotFound : string.Join(", ", items);

    private static bool IsSet(string name) => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));

    // ---- child processes ----

    // First stderr line of the last child process that exited non-zero (empty otherwise); the run is single-threaded.
    private static string s_lastError = string.Empty;

    /// <summary>Runs a tool and returns its trimmed standard output, or null when it is missing, fails or times out.</summary>
    private static string? Run(string fileName, string arguments, string? workingDirectory = null)
    {
        try
        {
            s_lastError = string.Empty;
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
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or AggregateException or NotSupportedException)
        {
            // Missing tool or blocked start: the caller reports "not found".
            Debug.WriteLine(ex.Message);
            return null;
        }
    }

    private static string FirstLine(string text)
    {
        int end = text.IndexOf('\n');
        return (end < 0 ? text : text.Substring(0, end)).Trim();
    }

    private static List<string> Lines(string? text)
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

    // ---- OS / hardware ----

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
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Debug.WriteLine(ex.Message);
            return null;
        }
    }

    private static string WindowsOs()
    {
        const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        string name = RegistryString(key, "ProductName") ?? "Windows";
        string build = RegistryString(key, "CurrentBuild") ?? Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
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
        return $"{label} ({Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}.{build}{ubr}), {Architecture()}";
    }

    private static string Architecture() => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    private static string LinuxOs()
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
        if (IsSet("WSL_DISTRO_NAME") || (procVersion is not null && procVersion.Contains("microsoft", StringComparison.OrdinalIgnoreCase)))
        {
            wsl = procVersion is not null && procVersion.Contains("WSL2", StringComparison.OrdinalIgnoreCase) ? ", WSL2" : ", WSL";
        }

        return Clean($"{name}, kernel {kernel}{wsl}, {Architecture()}");
    }

    private static string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex.Message);
            return null;
        }
    }

    private static string WindowsCpu()
    {
        string? name = RegistryString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
        return name is null ? NotFound : CollapseSpaces(name);
    }

    private static string LinuxCpu()
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

    private static string Memory(bool windows)
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
                    string number = line.Substring("MemTotal:".Length).Replace("kB", string.Empty, StringComparison.Ordinal).Trim();
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

    private static string WindowsBoard()
    {
        const string key = @"HARDWARE\DESCRIPTION\System\BIOS";
        return BoardText(
            RegistryString(key, "BaseBoardManufacturer"),
            RegistryString(key, "BaseBoardProduct"),
            RegistryString(key, "BIOSVersion"));
    }

    private static string LinuxBoard()
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

    // ---- GPU / CUDA ----

    private static void CollectGpus(ReportModel model)
    {
        string? query = Run("nvidia-smi", "--query-gpu=name,compute_cap,memory.total,driver_version --format=csv,noheader");
        string driverCuda = string.Empty;
        string? header = Run("nvidia-smi", string.Empty);
        if (header is not null)
        {
            // Older drivers print "CUDA Version:", newer ones "CUDA UMD Version:".
            string marker = "CUDA UMD Version:";
            int at = header.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
            {
                marker = "CUDA Version:";
                at = header.IndexOf(marker, StringComparison.Ordinal);
            }

            if (at >= 0)
            {
                string rest = header.Substring(at + marker.Length).TrimStart();
                int end = 0;
                while (end < rest.Length && (char.IsAsciiDigit(rest[end]) || rest[end] == '.'))
                {
                    end++;
                }

                if (end > 0)
                {
                    driverCuda = $" (driver CUDA {rest.Substring(0, end)})";
                }
            }
        }

        foreach (string line in Lines(query))
        {
            string[] parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 4)
            {
                continue;
            }

            string sm = parts[1].Replace(".", string.Empty, StringComparison.Ordinal);
            string memory = parts[2];
            if (memory.EndsWith("MiB", StringComparison.Ordinal)
                && double.TryParse(memory.Substring(0, memory.Length - 3).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double mib))
            {
                memory = $"{Math.Round(mib / 1024.0).ToString("0", CultureInfo.InvariantCulture)} GB";
            }

            model.Gpus.Add(Clean($"{parts[0]}, sm_{sm}, {memory}, driver {parts[3]}{driverCuda}"));
        }
    }

    private static string CudaToolkit()
    {
        string exe = OperatingSystem.IsWindows() ? "nvcc.exe" : "nvcc";
        string? output = Run("nvcc", "--version");
        if (output is null)
        {
            string? cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
            if (!string.IsNullOrEmpty(cudaPath))
            {
                output = Run(Path.Combine(cudaPath, "bin", exe), "--version");
            }
        }

        if (output is null && !OperatingSystem.IsWindows())
        {
            output = Run("/usr/local/cuda/bin/nvcc", "--version");
        }

        if (output is null)
        {
            return NotFound;
        }

        foreach (string line in Lines(output))
        {
            const string marker = "release ";
            int at = line.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                string rest = line.Substring(at + marker.Length);
                int comma = rest.IndexOf(',');
                string release = comma >= 0 ? rest.Substring(0, comma) : rest;
                string build = comma >= 0 ? rest.Substring(comma + 1).Trim() : string.Empty;
                return build.Length > 0 ? $"nvcc {release} ({build})" : $"nvcc {release}";
            }
        }

        return Clean(FirstLine(output));
    }

    // ---- host C++ ----

    private static string Msvc()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string vswhere = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            return NotFound;
        }

        const string query = "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64";
        string? installPath = Run(vswhere, query + " -property installationPath");
        if (installPath is null)
        {
            return NotFound;
        }

        string? toolset = ReadFile(Path.Combine(installPath.Trim(), "VC", "Auxiliary", "Build", "Microsoft.VCToolsVersion.default.txt"))?.Trim();
        string? vsVersion = Run(vswhere, query + " -property catalog_productDisplayVersion");
        string vs = vsVersion is null ? string.Empty : $" (Visual Studio {FirstLine(vsVersion)}, via vswhere)";
        return toolset is null ? $"MSVC {NotFound}{vs}" : $"MSVC {toolset}{vs}";
    }

    private static string LinuxCompilers()
    {
        string? gpp = Run("g++", "--version");
        string? clang = Run("clang++", "--version");
        if (gpp is null && clang is null)
        {
            return NotFound;
        }

        string gppText = gpp is null ? $"g++ {NotFound}" : Clean(FirstLine(gpp));
        string clangText = clang is null ? $"clang++ {NotFound}" : Clean(FirstLine(clang));
        return $"{gppText}; {clangText}";
    }

    // ---- .NET ----

    private static void CollectDotnet(ReportModel model)
    {
        string? slnx = FindUp("Henicos.slnx");
        string start = (slnx is null ? null : Path.GetDirectoryName(slnx)) ?? Directory.GetCurrentDirectory();
        string? version = Run("dotnet", "--version", start);
        model.DotnetSdk = version is null ? NotFound : FirstLine(version);

        string? globalJson = FindUp("global.json");
        if (globalJson is not null)
        {
            model.GlobalJson = PinnedSdk(globalJson) ?? "present, no sdk.version";
        }

        foreach (string line in Lines(Run("dotnet", "--list-sdks", start)))
        {
            model.DotnetSdks.Add(StripPath(line));
        }

        foreach (string line in Lines(Run("dotnet", "--list-runtimes", start)))
        {
            model.DotnetRuntimes.Add(StripPath(line));
        }
    }

    // "10.0.402 [C:\Program Files\dotnet\sdk]" becomes "10.0.402": install paths are not needed.
    private static string StripPath(string line)
    {
        int bracket = line.IndexOf(" [", StringComparison.Ordinal);
        return bracket < 0 ? line : line.Substring(0, bracket);
    }

    private static string? PinnedSdk(string globalJsonPath)
    {
        try
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(globalJsonPath), options);
            if (document.RootElement.TryGetProperty("sdk", out JsonElement sdk)
                && sdk.TryGetProperty("version", out JsonElement version)
                && version.ValueKind == JsonValueKind.String)
            {
                return version.GetString();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex.Message);
        }

        return null;
    }

    private static string? FindUp(string fileName)
    {
        try
        {
            DirectoryInfo? current = new(Directory.GetCurrentDirectory());
            while (current is not null)
            {
                string candidate = Path.Combine(current.FullName, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex.Message);
        }

        return null;
    }

    // ---- checkout / environment variables ----

    private static string? GSharpCheckout()
    {
        string? slnx = FindUp("Henicos.slnx");
        if (slnx is null)
        {
            return null;
        }

        string repo = Path.GetDirectoryName(slnx) ?? Directory.GetCurrentDirectory();
        string? commit = Run("git", "rev-parse --short HEAD", repo);
        if (commit is null)
        {
            // Inside a clone, so say why git failed (dubious ownership, a worktree whose .git file points to another OS, ...).
            string reason = s_lastError.Length > 0 ? s_lastError : "no output";
            return Clean($"git failed: {reason}");
        }

        string branch = Run("git", "branch --show-current", repo) ?? "detached";
        string? porcelain = Run("git", "status --porcelain", repo);
        return Clean($"commit {FirstLine(commit)} ({FirstLine(branch)}, {(porcelain is null ? "clean" : "dirty")})");
    }

    private static void CollectEnvironmentVariables(ReportModel model)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal) { "GSHARP_TEST_NO_CUDA", "CUDA_VISIBLE_DEVICES" };
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && key.StartsWith("GSHARP_", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(key);
            }
        }

        foreach (string name in names)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            model.EnvironmentVariables[name] = string.IsNullOrEmpty(value) ? "(unset)" : Clean(value);
        }
    }
}
