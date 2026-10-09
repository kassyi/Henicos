using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>MSBuild task that compiles C++ host shadow implementations into native platform dynamic-link libraries (.dll) via MSVC.</summary>
public class CompileCppToNativeTask : CompileKernelTaskBase
{
    /// <summary>Gets or sets the C++ source files to compile.</summary>
    [Required]
    public ITaskItem[] CppSources { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether to compile for multiple architectures (x64 and arm64).</summary>
    public bool GSharpMultiTarget
    {
        get; set;
    }

    /// <summary>Gets or sets the resulting compiled native DLL task items.</summary>
    [Output]
    public ITaskItem[] OutputDlls { get; set; } = [];

    private string[] _architectures = [];
    private readonly Dictionary<string, Dictionary<string, string>> _vcvarsEnvs = [];

    /// <inheritdoc />
    protected override ITaskItem[] GetSources() => CppSources;

    /// <inheritdoc />
    protected override void SetOutputItems(ITaskItem[] outputItems) => OutputDlls = outputItems;

    /// <inheritdoc />
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
        var items = new ConcurrentBag<ITaskItem>();

        var hasErrorsBox = new[] { 0 };

        Parallel.ForEach(_architectures, arch =>
        {
            if (Volatile.Read(ref hasErrorsBox[0]) != 0)
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


            if (MockCompilation)
            {
                File.WriteAllText(outputDllPath, "Mock Native DLL Content");
                var mockTaskItem = new TaskItem(outputDllPath);
                mockTaskItem.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(outputDllPath)}");
                items.Add(mockTaskItem);

                return;
            }

            if (!CompileWithMsvc(sourcePath, outputDllPath, vcvarsArch) ||
                !ArtifactHelper.ProcessArtifact(outputDllPath, Log))
            {
                Interlocked.Exchange(ref hasErrorsBox[0], 1);
                return;
            }

            var taskItem = new TaskItem(outputDllPath);
            taskItem.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(outputDllPath)}");
            items.Add(taskItem);
        });

        return Volatile.Read(ref hasErrorsBox[0]) != 0 ? null : [.. items];
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
        // /openmp = Enable OpenMP for BSP CPU scheduler parallelism

        var clArgs = $"/O2 /LD /nologo /std:c++17 /openmp /Zc:preprocessor \"{sourcePath}\" /Fe:\"{outputDllPath}\"";

        Log.LogMessage(MessageImportance.Normal, $"Compiling C++ source for {vcvarsArch}: {sourcePath}");

        var clExePath = "cl.exe";
        if (_vcvarsEnvs.TryGetValue(vcvarsArch, out var env))
        {
            if (env.TryGetValue("PATH", out var pathEnv))
            {
                var span = pathEnv.AsSpan();
                while (span.Length > 0)
                {
                    var sepIdx = span.IndexOf(Path.PathSeparator);
                    var segment = sepIdx >= 0 ? span.Slice(0, sepIdx) : span;
                    var fullPath = Path.Combine(segment.ToString(), "cl.exe");
                    if (File.Exists(fullPath))
                    {
                        clExePath = fullPath;
                        break;
                    }

                    if (sepIdx < 0)
                    {
                        break;
                    }

                    span = span.Slice(sepIdx + 1);
                }
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = clExePath,
            Arguments = clArgs
        };

        if (env != null)
        {
            foreach (var kvp in env)
            {
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        try
        {
            var result = ProcessRunner.Default.Run(startInfo);

            if (result.ExitCode == 0 && File.Exists(outputDllPath))
            {
                // MSVC produces .obj, .lib, .exp. Clean them up to avoid clutter.
                var baseDir = Path.GetDirectoryName(outputDllPath) ?? "";
                var baseName = Path.GetFileNameWithoutExtension(outputDllPath);
                var objPath = Path.Combine(baseDir, baseName + ".obj");
                var libPath = Path.Combine(baseDir, baseName + ".lib");
                var expPath = Path.Combine(baseDir, baseName + ".exp");

                // Also the source .obj (if cl.exe drops it in the source folder, but typically it drops in cwd or output dir)
                var srcDir = Path.GetDirectoryName(sourcePath) ?? "";
                var srcObjPath = Path.Combine(srcDir,
                    Path.GetFileNameWithoutExtension(sourcePath) + ".obj");
                var cwdObjPath = Path.Combine(Environment.CurrentDirectory,
                    Path.GetFileNameWithoutExtension(sourcePath) + ".obj");

                SafeDeleteFiles(objPath, libPath, expPath, srcObjPath, cwdObjPath);

                return true;
            }

            Log.LogError($"MSVC compilation failed with exit code {result.ExitCode}.");
            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                Log.LogMessage(MessageImportance.High, result.StandardOutput);
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                Log.LogError(result.StandardError);
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to start cl.exe. Ensure MSVC is installed. Details: {ex.Message}");
            return false;
        }
    }
}
