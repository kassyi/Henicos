using System.Collections.Concurrent;

namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>Base MSBuild task providing parallel compilation, Visual Studio environment resolution, and artifact dispatching for kernel sources.</summary>
public abstract class CompileKernelTaskBase : Microsoft.Build.Utilities.Task
{
    /// <summary>Gets or sets the target directory where compiled artifacts are placed.</summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether to generate mock compilation outputs during testing.</summary>
    public bool MockCompilation
    {
        get; set;
    }

    /// <summary>Gets or sets the process runner to use for executing external tools.</summary>
    public IProcessRunner ProcessRunner { get; set; } = Helpers.ProcessRunner.Default;

    /// <summary>Retrieves source items to be processed by this compilation task.</summary>
    protected abstract ITaskItem[] GetSources();

    /// <summary>Compiles or transforms an individual source item into output artifacts.</summary>
    protected abstract ITaskItem[]? ProcessSourceItem(ITaskItem sourceItem);

    /// <summary>Sets the output task items produced by compilation.</summary>
    protected abstract void SetOutputItems(ITaskItem[] outputItems);

    /// <inheritdoc />
    public override bool Execute()
    {
        var sources = GetSources();
        if (sources.Length == 0)
        {
            return true;
        }

        if (!Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }

        var outputItems = new ConcurrentBag<ITaskItem>();

        if (!OnBeforeExecute())
        {
            return false;
        }

        var hasErrors = false;

        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };
        Parallel.ForEach(sources, options, sourceItem =>
        {
            if (hasErrors)
            {
                return;
            }

            var taskItems = ProcessSourceItem(sourceItem);
            if (taskItems == null)
            {
                hasErrors = true;
                return;
            }

            foreach (var item in taskItems)
            {
                outputItems.Add(item);
            }
        });

        if (hasErrors)
        {
            return false;
        }

        SetOutputItems([.. outputItems]);
        return true;
    }

    protected virtual bool OnBeforeExecute() => true;

    private static readonly ConcurrentDictionary<string, Lazy<Dictionary<string, string>?>> s_vcvarsCache =
        new(StringComparer.OrdinalIgnoreCase);

    public bool DisableVcvarsCache { get; set; } = false;

    protected Dictionary<string, string>? GetVcvarsEnvironment(string vcvarsArch)
    {
        if (DisableVcvarsCache)
        {
            return ResolveVcvarsEnvironment(vcvarsArch);
        }

        return s_vcvarsCache.GetOrAdd(vcvarsArch,
            arch => new Lazy<Dictionary<string, string>?>(() => ResolveVcvarsEnvironment(arch))).Value;
    }

    private Dictionary<string, string>? ResolveVcvarsEnvironment(string vcvarsArch)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var vswhere = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");

        if (!File.Exists(vswhere))
        {
            return null;
        }

        var vswhereStartInfo = new ProcessStartInfo
        {
            FileName = vswhere,
            Arguments =
                "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            EnvironmentVariables =
            {
                ["VSCMD_SKIP_SENDTELEMETRY"] = "1"
            }
        };

        var vswhereResult = this.ProcessRunner.Run(vswhereStartInfo);
        if (vswhereResult.ExitCode != 0)
        {
            return null;
        }

        var output = vswhereResult.StandardOutput.AsSpan().Trim().ToString();

        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        var vcvarsPath = Path.Combine(output, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        if (!File.Exists(vcvarsPath))
        {
            return null;
        }

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

        var envResult = this.ProcessRunner.Run(envStartInfo);
        if (envResult.ExitCode != 0)
        {
            return null;
        }

        var envOutput = envResult.StandardOutput;

        var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(envOutput);
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

    protected void SafeDeleteFiles(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore cleanup errors on best-effort basis
            }
        }
    }
}
