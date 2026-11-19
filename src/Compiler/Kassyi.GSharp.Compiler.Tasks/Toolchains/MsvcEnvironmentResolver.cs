namespace Kassyi.GSharp.Compiler.Tasks.Toolchains;

/// <summary>
/// Finds Visual Studio with <c>vswhere.exe</c>, runs <c>vcvarsall.bat</c> to read the build environment of an
/// architecture, and reads the version of the <c>cl.exe</c> found in it. <see cref="MsvcToolchain"/> builds the
/// compile and link commands on top of it.
/// </summary>
/// <param name="processRunner">The process runner used to invoke <c>vswhere.exe</c> and <c>cmd.exe</c>.</param>
/// <param name="disableCache">If <see langword="true"/>, bypasses the static <c>vcvarsall.bat</c> environment cache.</param>
/// <param name="vswherePathOverride">Optional explicit path to <c>vswhere.exe</c>.</param>
internal sealed class MsvcEnvironmentResolver(
    IProcessRunner processRunner,
    bool disableCache,
    string? vswherePathOverride)
{
    private static readonly ConcurrentDictionary<string, Lazy<Dictionary<string, string>?>> s_vcvarsCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _resolvedEnvs =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps a host architecture name (<c>x64</c>, <c>arm64</c>) to the <c>vcvarsall.bat</c> argument.</summary>
    public static string ToVcvarsArchitecture(string arch) => arch switch
    {
        "x64" => "x64",
        "arm64" => "x64_arm64",
        _ => arch
    };

    /// <summary>
    /// Resolves or retrieves the cached <c>vcvarsall.bat</c> environment variables for the specified architecture.
    /// </summary>
    /// <param name="vcvarsArch">The <c>vcvarsall.bat</c> architecture specification (e.g., <c>x64</c>, <c>x64_arm64</c>).</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A dictionary of environment variables, or <see langword="null"/> if resolution failed.</returns>
    public Dictionary<string, string>? GetVcvarsEnvironment(
        string vcvarsArch,
        CancellationToken cancellationToken = default)
    {
        if (disableCache)
        {
            return ResolveVcvarsEnvironment(vcvarsArch, cancellationToken);
        }

        return s_vcvarsCache.GetOrAdd(
            vcvarsArch,
            arch => new Lazy<Dictionary<string, string>?>(() => ResolveVcvarsEnvironment(arch, cancellationToken))).Value;
    }

    /// <summary>Remembers the environment that <see cref="MsvcToolchain.PrepareEnvironment"/> resolved for an architecture.</summary>
    public void Remember(string vcvarsArch, Dictionary<string, string> environment) =>
        _resolvedEnvs[vcvarsArch] = environment;

    /// <summary>
    /// Gets the environment remembered for the architecture, or resolves it without remembering it.
    /// </summary>
    public Dictionary<string, string>? GetEnvironment(string vcvarsArch)
    {
        if (_resolvedEnvs.TryGetValue(vcvarsArch, out var env))
        {
            return env;
        }

        return GetVcvarsEnvironment(vcvarsArch);
    }

    // The resolved vcvarsall environment of the architecture, or null when it cannot be resolved.
    public Dictionary<string, string>? ResolveCompileEnvironment(
        string vcvarsArch,
        CancellationToken cancellationToken)
    {
        if (_resolvedEnvs.TryGetValue(vcvarsArch, out var resolved))
        {
            return resolved;
        }

        resolved = GetVcvarsEnvironment(vcvarsArch, cancellationToken);
        if (resolved is not null)
        {
            _resolvedEnvs[vcvarsArch] = resolved;
        }

        return resolved;
    }

    /// <summary>Reads the version of <paramref name="clPath"/>, or <c>VCToolsVersion</c> of the environment, or <c>unknown</c>.</summary>
    public static string ReadMsvcVersion(string clPath, Dictionary<string, string>? env)
    {
        if (File.Exists(clPath))
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(clPath);
                if (!string.IsNullOrWhiteSpace(info.ProductVersion))
                {
                    return info.ProductVersion.AsSpan().Trim().ToString();
                }

                if (!string.IsNullOrWhiteSpace(info.FileVersion))
                {
                    return info.FileVersion.AsSpan().Trim().ToString();
                }
            }
            // smell-scan: ignore swallowed-exception -- 0-byte fake cl.exe in tests; fall back to VCToolsVersion
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Fall back to VCToolsVersion below.
            }
        }

        if (env is not null &&
            env.TryGetValue("VCToolsVersion", out var vcTools) &&
            !string.IsNullOrWhiteSpace(vcTools))
        {
            return vcTools.AsSpan().Trim().ToString();
        }

        return "unknown";
    }

    private Dictionary<string, string>? ResolveVcvarsEnvironment(
        string vcvarsArch,
        CancellationToken cancellationToken)
    {
        var vswhere = LocateVswhere();
        if (vswhere == null)
        {
            return null;
        }

        var installationPath = QueryInstallationPath(vswhere, cancellationToken);
        if (string.IsNullOrEmpty(installationPath))
        {
            return null;
        }

        var vcvarsPath = Path.Combine(installationPath, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        if (!File.Exists(vcvarsPath))
        {
            return null;
        }

        return RunVcvars(vcvarsPath, vcvarsArch, cancellationToken);
    }

    private string? LocateVswhere()
    {
        if (vswherePathOverride is { Length: > 0 })
        {
            return vswherePathOverride;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var vswhere = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        return File.Exists(vswhere) ? vswhere : null;
    }

    // The vswhere query below is repeated by tools/collect-env/ToolchainProbe.cs (Msvc); keep both in sync.
    private string? QueryInstallationPath(string vswhere, CancellationToken cancellationToken)
    {
        var vswhereStartInfo = new ProcessStartInfo
        {
            FileName = vswhere,
            Arguments = "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 " +
                "-property installationPath",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            EnvironmentVariables =
            {
                ["VSCMD_SKIP_SENDTELEMETRY"] = "1"
            }
        };

        var vswhereResult = processRunner.Run(vswhereStartInfo, cancellationToken);
        return vswhereResult.ExitCode == 0 ? vswhereResult.StandardOutput.AsSpan().Trim().ToString() : null;
    }

    // Runs vcvarsall.bat in cmd.exe and reads the environment it leaves (the output of `set`).
    private Dictionary<string, string>? RunVcvars(
        string vcvarsPath,
        string vcvarsArch,
        CancellationToken cancellationToken)
    {
        var envStartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"set VSCMD_SKIP_SENDTELEMETRY=1 && call \"{vcvarsPath}\" {vcvarsArch} >nul && set\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            EnvironmentVariables =
            {
                ["VSCMD_SKIP_SENDTELEMETRY"] = "1"
            }
        };

        var envResult = processRunner.Run(envStartInfo, cancellationToken);
        return envResult.ExitCode == 0 ? ParseEnvironment(envResult.StandardOutput) : null;
    }

    private static Dictionary<string, string> ParseEnvironment(string setOutput)
    {
        var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(setOutput);
        while (reader.ReadLine() is { } line)
        {
            var eqIndex = line.IndexOf('=');
            if (eqIndex > 0)
            {
                envVars[line[..eqIndex]] = line[(eqIndex + 1)..];
            }
        }

        return envVars;
    }
}
