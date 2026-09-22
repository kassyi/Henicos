using System.Text;
using System.Text.RegularExpressions;

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
    private bool _skipCompilation;

    protected override ITaskItem[] GetSources() => CudaSources;
    protected override void SetOutputItems(ITaskItem[] outputItems) => OutputFatbins = outputItems;

    private Dictionary<string, string>? _vcvarsEnv;

    protected override bool OnBeforeExecute()
    {
        if (MockCompilation)
        {
            return true;
        }

        if (Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA") == "1")
        {
            Log.LogMessage(MessageImportance.High, "GSHARP_TEST_NO_CUDA is set. Skipping CUDA fatbin compilation.");
            _skipCompilation = true;
            return true;
        }

        if (!IsNvccAvailable())
        {
            Log.LogWarning(
                $"CUDA compiler (nvcc) was not found at '{NvccPath}'. Skipping CUDA fatbin compilation. Set GSHARP_TEST_NO_CUDA=1 to suppress this warning in non-CUDA environments.");
            _skipCompilation = true;
            return true;
        }

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

    private bool IsNvccAvailable()
    {
        try
        {
            var checkInfo = new ProcessStartInfo
            {
                FileName = NvccPath,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(checkInfo);
            if (p == null)
            {
                return false;
            }

            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    protected override ITaskItem[]? ProcessSourceItem(ITaskItem sourceItem)
    {
        if (_skipCompilation)
        {
            return [];
        }

        var sourcePath = sourceItem.GetMetadata("FullPath");
        var fileName = ArtifactHelper.GetCleanKernelFileName(sourcePath);
        var outputFatbinPath = Path.Combine(OutputDirectory, $"{fileName}.fatbin");

        if (MockCompilation)
        {
            File.WriteAllText(outputFatbinPath, "Mock Fatbin Content");
            var mockTaskItem = new TaskItem(outputFatbinPath);
            mockTaskItem.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(outputFatbinPath)}");
            return [mockTaskItem];
        }

        if (!CompileWithNvcc(sourcePath, outputFatbinPath, _architectures))
        {
            return null;
        }

        if (!ArtifactHelper.ProcessArtifact(outputFatbinPath, Log))
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
        args.Append("--ptxas-options=-v,--warn-on-spills ");
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

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to start nvcc at '{NvccPath}': {ex.Message}");
            return false;
        }

        if (process == null)
        {
            Log.LogError(
                $"Failed to start nvcc at '{NvccPath}'. Please ensure CUDA Toolkit is installed and nvcc is in PATH.");
            return false;
        }

        using (process)
        {
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

            if (NvccOutputParser.DetectRegisterSpill(output, error, out var errorMessage))
            {
                Log.LogWarning(errorMessage);
            }

            if (process.ExitCode == 0)
            {
                if (!string.IsNullOrWhiteSpace(error) &&
                    error.IndexOf("ptxas info", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Log.LogMessage(MessageImportance.Low, error);
                }

                return true;
            }

            if (process.ExitCode != 0)
            {
                Log.LogError($"nvcc compilation failed with exit code {process.ExitCode}.");
            }

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
}

internal static class NvccOutputParser
{
    private static readonly Regex s_spillRegex =
        new(@"[1-9]\d*\s+bytes spill", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool DetectRegisterSpill(string output, string error, out string errorMessage)
    {
        errorMessage = "";
        var hasSpill = false;

        // Check for actual warnings or non-zero spill bytes
        if (error.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0 &&
            error.IndexOf("spill", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            hasSpill = true;
        }
        else if (s_spillRegex.IsMatch(error) || s_spillRegex.IsMatch(output))
        {
            hasSpill = true;
        }

        if (!hasSpill)
        {
            return false;
        }

        errorMessage =
            "Register spill detected during PTX compilation. " +
            "Please reduce register pressure (e.g. by avoiding excessive inlining or large stack allocations in GPU kernels).";
        return true;
    }
}
