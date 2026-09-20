namespace Kassyi.GSharp.Compiler.Tasks;

public abstract class CompileKernelTaskBase : Microsoft.Build.Utilities.Task
{
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    public bool MockCompilation { get; set; }

    protected abstract ITaskItem[] GetSources();
    protected abstract ITaskItem[]? ProcessSourceItem(ITaskItem sourceItem);
    protected abstract void SetOutputItems(ITaskItem[] outputItems);

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

        var outputItems = new List<ITaskItem>();

        if (!OnBeforeExecute())
        {
            return false;
        }

        var hasErrors = false;
        var lockObj = new object();

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

            lock (lockObj)
            {
                outputItems.AddRange(taskItems);
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

    private static readonly Dictionary<string, Dictionary<string, string>?> s_vcvarsCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object s_vcvarsLock = new();

    protected Dictionary<string, string>? GetVcvarsEnvironment(string vcvarsArch)
    {
        lock (s_vcvarsLock)
        {
            if (s_vcvarsCache.TryGetValue(vcvarsArch, out var cachedEnv))
            {
                return cachedEnv;
            }
        }

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

        using var vswhereProcess = Process.Start(vswhereStartInfo);
        if (vswhereProcess == null)
        {
            return null;
        }

        var output = vswhereProcess.StandardOutput.ReadToEnd().Trim();
        vswhereProcess.WaitForExit();

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

        using var envProcess = Process.Start(envStartInfo);
        if (envProcess == null)
        {
            return null;
        }

        var envOutput = envProcess.StandardOutput.ReadToEnd();
        envProcess.WaitForExit();

        var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in envOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIndex = line.IndexOf('=');
            if (eqIndex > 0)
            {
                envVars[line.Substring(0, eqIndex)] = line.Substring(eqIndex + 1);
            }
        }

        lock (s_vcvarsLock)
        {
            s_vcvarsCache[vcvarsArch] = envVars;
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
