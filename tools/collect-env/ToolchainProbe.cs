using System.Text.Json;
using static Collector;
using static Privacy;
using static ProcessProbe;

/// <summary>Reads the host C++ compilers and the .NET SDKs and runtimes.</summary>
internal static class ToolchainProbe
{
    internal static string Msvc()
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

        string toolsetFile = Path.Combine(
            installPath.Trim(),
            "VC",
            "Auxiliary",
            "Build",
            "Microsoft.VCToolsVersion.default.txt"
        );
        string? toolset = ReadFile(toolsetFile)?.Trim();
        string? vsVersion = Run(vswhere, query + " -property catalog_productDisplayVersion");
        string vs = vsVersion is null ? string.Empty : $" (Visual Studio {FirstLine(vsVersion)}, via vswhere)";
        return toolset is null ? $"MSVC {NotFound}{vs}" : $"MSVC {toolset}{vs}";
    }

    internal static string LinuxCompilers()
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

    internal static void CollectDotnet(ReportModel model)
    {
        string? slnx = CheckoutProbe.FindUp("Henicos.slnx");
        string start = (slnx is null ? null : Path.GetDirectoryName(slnx)) ?? Directory.GetCurrentDirectory();
        string? version = Run("dotnet", "--version", start);
        model.DotnetSdk = version is null ? NotFound : FirstLine(version);

        string? globalJson = CheckoutProbe.FindUp("global.json");
        if (globalJson is not null)
        {
            model.GlobalJson = PinnedSdk(globalJson);
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

    /// <summary>Returns the SDK version global.json pins, or why there is none.</summary>
    private static string PinnedSdk(string globalJsonPath)
    {
        try
        {
            var options = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(globalJsonPath), options);
            if (document.RootElement.TryGetProperty("sdk", out JsonElement sdk)
                && sdk.TryGetProperty("version", out JsonElement version)
                && version.ValueKind == JsonValueKind.String)
            {
                return version.GetString() ?? string.Empty;
            }

            return "present, no sdk.version";
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Clean($"unreadable ({ex.Message})");
        }
    }
}
