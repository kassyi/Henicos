using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Kassyi.GSharp.Compiler.Tasks;

public class CompileCudaToFatbinTask : CompileKernelTaskBase
{
    [Required]
    public ITaskItem[] CudaSources { get; set; } = [];

    public string TargetArchitectures { get; set; } =
        "compute_50,sm_50;compute_70,sm_70;compute_80,sm_80;compute_86,sm_86";

    public string NvccPath { get; set; } = "nvcc";

    [Output]
    public ITaskItem[] OutputFatbins { get; set; } = [];

    private string[] _architectures = [];

    protected override ITaskItem[] GetSources() => CudaSources;
    protected override void SetOutputItems(ITaskItem[] outputItems) => OutputFatbins = outputItems;

    protected override bool OnBeforeExecute()
    {
        _architectures = TargetArchitectures.Split([';'], StringSplitOptions.RemoveEmptyEntries);
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

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            Log.LogError(
                $"Failed to start nvcc at '{NvccPath}'. Please ensure CUDA Toolkit is installed and nvcc is in PATH.");
            return false;
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

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