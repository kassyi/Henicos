using System.Text;

namespace Kassyi.GSharp.Compiler.Tasks;

public class CompileCudaToFatbinTask : CompileKernelTaskBase
{
    [Required]
    public ITaskItem[] CudaSources { get; set; } = [];

    public string TargetArchitectures { get; set; } =
        "compute_86,sm_86";

    public string NvccPath { get; set; } = "nvcc";

    [Output]
    public ITaskItem[] OutputFatbins { get; set; } = [];

    private string[] _architectures = [];

    protected override ITaskItem[] GetSources() => CudaSources;
    protected override void SetOutputItems(ITaskItem[] outputItems) => OutputFatbins = outputItems;

    private Dictionary<string, string>? _vcvarsEnv;

    protected override bool OnBeforeExecute()
    {
        _architectures = TargetArchitectures.Split([';'], StringSplitOptions.RemoveEmptyEntries);
        _vcvarsEnv = GetVcvarsEnvironment("x64");
        if (_vcvarsEnv == null)
        {
            Log.LogError(
                "Could not locate vcvarsall.bat via vswhere.exe. Please ensure MSVC C++ Build Tools are installed.");
            return false;
        }

        return true;
    }

    protected override ITaskItem[]? ProcessSourceItem(ITaskItem sourceItem)
    {
        var sourcePath = sourceItem.GetMetadata("FullPath");
        var fileName = ArtifactHelper.GetCleanKernelFileName(sourcePath);
        var outputFatbinPath = Path.Combine(OutputDirectory, $"{fileName}.fatbin");
        var keyFilePath = sourcePath + ".key";

        if (!CompileWithNvcc(sourcePath, outputFatbinPath, _architectures))
        {
            return null;
        }

        if (!ArtifactHelper.ProcessArtifact(outputFatbinPath, keyFilePath, Log))
        {
            return null;
        }

        var taskItem = new TaskItem(outputFatbinPath);
        taskItem.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(outputFatbinPath)}");
        return [taskItem];
    }

    private bool CompileWithNvcc(string sourcePath, string outputFatbinPath, string[] architectures)
    {
        var args = BuildNvccArguments(sourcePath, outputFatbinPath, architectures);
        Log.LogMessage(MessageImportance.Normal, $"Compiling CUDA source: {sourcePath}");

        if (!RunNvccProcess(args))
        {
            return false;
        }

        if (File.Exists(outputFatbinPath))
        {
            return true;
        }

        Log.LogError($"nvcc succeeded but output fatbin was not found at '{outputFatbinPath}'.");
        return false;
    }

    private string BuildNvccArguments(string sourcePath, string outputFatbinPath, string[] architectures)
    {
        var args = new StringBuilder();
        args.Append("--fatbin ");
        args.Append("-std=c++17 ");
        args.Append("-DCCCL_IGNORE_MSVC_TRADITIONAL_PREPROCESSOR_WARNING ");
        args.Append("-Xcompiler \"/wd4819\" ");
        foreach (var arch in architectures)
        {
            var parts = arch.Split(',');
            if (parts.Length == 2)
            {
                args.Append($"-gencode arch={parts[0]},code={parts[1]} ");
            }
        }

        args.Append($"-o \"{outputFatbinPath}\" \"{sourcePath}\"");
        return args.ToString();
    }

    private bool RunNvccProcess(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = NvccPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (_vcvarsEnv != null)
        {
            foreach (var kvp in _vcvarsEnv)
            {
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            Log.LogError(
                $"Failed to start nvcc at '{NvccPath}'. Please ensure CUDA Toolkit is installed and nvcc is in PATH.");
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

        if (process.ExitCode == 0)
        {
            return true;
        }

        Log.LogError($"nvcc compilation failed with exit code {process.ExitCode}.");
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