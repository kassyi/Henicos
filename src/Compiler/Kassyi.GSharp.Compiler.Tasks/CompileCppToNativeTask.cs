using System.Runtime.InteropServices;
using System.Text;

namespace Kassyi.GSharp.Compiler.Tasks;

public class CompileCppToNativeTask : CompileKernelTaskBase
{
    [Required]
    public ITaskItem[] CppSources { get; set; } = [];

    public bool GSharpMultiTarget { get; set; }

    [Output]
    public ITaskItem[] OutputDlls { get; set; } = [];

    private string[] _architectures = [];
    private readonly Dictionary<string, Dictionary<string, string>> _vcvarsEnvs = [];

    protected override ITaskItem[] GetSources() => CppSources;
    protected override void SetOutputItems(ITaskItem[] outputItems) => OutputDlls = outputItems;

    protected override bool OnBeforeExecute()
    {
        _architectures = GetTargetArchitectures();

        if (MockCompilation)
        {
            return true;
        }

        foreach (var arch in _architectures)
        {
            var vcvarsArch = arch switch
            {
                "x64" => "x64",
                "arm64" => "x64_arm64",
                _ => "x64"
            };
            var env = GetVcvarsEnvironment(vcvarsArch);

            if (env == null)
            {
                Log.LogError(
                    $"Could not locate vcvarsall.bat via vswhere.exe for architecture {vcvarsArch}. Please ensure MSVC C++ Build Tools are installed.");
                return false;
            }

            _vcvarsEnvs[vcvarsArch] = env;
        }

        return true;
    }

    protected override ITaskItem[]? ProcessSourceItem(ITaskItem sourceItem)
    {
        var sourcePath = sourceItem.GetMetadata("FullPath");
        var fileName = ArtifactHelper.GetCleanKernelFileName(sourcePath);
        var items = new List<ITaskItem>();

        var hasErrors = false;
        var lockObj = new object();

        Parallel.ForEach(_architectures, arch =>
        {
            if (hasErrors)
            {
                return;
            }

            // cl.exe architecture argument
            var vcvarsArch = arch switch
            {
                "x64" => "x64",
                "arm64" => "x64_arm64",
                _ => "x64"
            };

            var outputDllPath = Path.Combine(OutputDirectory, $"{fileName}_{arch}.dll");

            lock (lockObj)
            {
                if (!Directory.Exists(OutputDirectory))
                {
                    Directory.CreateDirectory(OutputDirectory);
                }
            }

            if (File.Exists(outputDllPath) &&
                File.GetLastWriteTimeUtc(outputDllPath) >= File.GetLastWriteTimeUtc(sourcePath))
            {
                var cachedTaskItem = new TaskItem(outputDllPath);
                cachedTaskItem.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(outputDllPath)}");
                lock (lockObj)
                {
                    items.Add(cachedTaskItem);
                }

                return;
            }

            if (MockCompilation)
            {
                File.WriteAllText(outputDllPath, "Mock Native DLL Content");
                var mockTaskItem = new TaskItem(outputDllPath);
                mockTaskItem.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(outputDllPath)}");
                lock (lockObj)
                {
                    items.Add(mockTaskItem);
                }

                return;
            }

            if (!CompileWithMsvc(sourcePath, outputDllPath, vcvarsArch))
            {
                hasErrors = true;
                return;
            }

            if (!ArtifactHelper.ProcessArtifact(outputDllPath, Log))
            {
                hasErrors = true;
                return;
            }

            var taskItem = new TaskItem(outputDllPath);
            taskItem.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(outputDllPath)}");
            lock (lockObj)
            {
                items.Add(taskItem);
            }
        });

        return hasErrors ? null : [.. items];
    }

    private string[] GetTargetArchitectures()
    {
        if (GSharpMultiTarget)
        {
            return ["x64", "arm64"];
        }

        // Determine current host architecture
        var arch = RuntimeInformation.ProcessArchitecture;
        return arch == Architecture.Arm64 ? ["arm64"] : ["x64"];
    }

    private bool CompileWithMsvc(string sourcePath, string outputDllPath, string vcvarsArch)
    {
        // /O2 = Optimize for speed
        // /LD = Create DLL
        // /Fe = Output file name
        // /nologo = Suppress copyright message
        // /Zc:preprocessor = Fix CUDA 13.3+ fatal error C1189 with MSVC
        // /std:c++17 = Fix CUDA 13.3+ libcu++ requirement

        var clArgs = $"/O2 /LD /nologo /std:c++17 /Zc:preprocessor \"{sourcePath}\" /Fe:\"{outputDllPath}\"";

        Log.LogMessage(MessageImportance.Normal, $"Compiling C++ source for {vcvarsArch}: {sourcePath}");

        var clExePath = "cl.exe";
        if (_vcvarsEnvs.TryGetValue(vcvarsArch, out var env))
        {
            if (env.TryGetValue("PATH", out var pathEnv))
            {
                foreach (var path in pathEnv.Split(Path.PathSeparator))
                {
                    var fullPath = Path.Combine(path, "cl.exe");
                    if (File.Exists(fullPath))
                    {
                        clExePath = fullPath;
                        break;
                    }
                }
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = clExePath,
            Arguments = clArgs,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (env != null)
        {
            foreach (var kvp in env)
            {
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            Log.LogError("Failed to start cl.exe. Ensure MSVC is installed.");
            return false;
        }

        var errorBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
            }
        };
        process.BeginErrorReadLine();

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        var error = errorBuilder.ToString();

        if (process.ExitCode == 0 && File.Exists(outputDllPath))
        {
            // MSVC produces .obj, .lib, .exp. Clean them up to avoid clutter.
            var baseDir = Path.GetDirectoryName(outputDllPath)!;
            var baseName = Path.GetFileNameWithoutExtension(outputDllPath);
            var objPath = Path.Combine(baseDir, baseName + ".obj");
            var libPath = Path.Combine(baseDir, baseName + ".lib");
            var expPath = Path.Combine(baseDir, baseName + ".exp");

            // Also the source .obj (if cl.exe drops it in the source folder, but typically it drops in cwd or output dir)
            var srcObjPath = Path.Combine(Path.GetDirectoryName(sourcePath)!,
                Path.GetFileNameWithoutExtension(sourcePath) + ".obj");
            var cwdObjPath = Path.Combine(Environment.CurrentDirectory,
                Path.GetFileNameWithoutExtension(sourcePath) + ".obj");

            SafeDeleteFiles(objPath, libPath, expPath, srcObjPath, cwdObjPath);

            return true;
        }

        Log.LogError($"MSVC compilation failed with exit code {process.ExitCode}.");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Log.LogMessage(MessageImportance.High, output);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            Log.LogError(error);
        }

        return false;
    }
}
