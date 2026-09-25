using System.Text;
using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler.Tasks.Helpers;

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
        if (_vcvarsEnv != null)
        {
            return true;
        }

        Log.LogError(
            "Could not locate vcvarsall.bat via vswhere.exe. Please ensure MSVC C++ Build Tools are installed.");
        return false;
    }

    private bool IsNvccAvailable()
    {
        try
        {
            var checkInfo = new ProcessStartInfo
            {
                FileName = NvccPath,
                Arguments = "--version"
            };
            var result = ProcessRunner.Default.Run(checkInfo);
            return result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.LogMessage(MessageImportance.Low, $"CUDA compiler check failed: {ex.Message}");
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

        if (File.Exists(outputFatbinPath) && File.Exists(sourcePath))
        {
            var sourceInfo = new FileInfo(sourcePath);
            var destInfo = new FileInfo(outputFatbinPath);
            if (destInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc)
            {
                Log.LogMessage(MessageImportance.Low,
                    $"Skipping compilation of '{sourcePath}' because '{outputFatbinPath}' is up-to-date.");
                var cachedItem = new TaskItem(outputFatbinPath);
                cachedItem.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(outputFatbinPath)}");
                return [cachedItem];
            }
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

        if (!RunNvccProcess(sourcePath, args))
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

    private bool RunNvccProcess(string sourcePath, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = NvccPath,
            Arguments = arguments
        };

        if (_vcvarsEnv != null)
        {
            foreach (var kvp in _vcvarsEnv)
            {
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        try
        {
            var result = ProcessRunner.Default.Run(startInfo);
            var output = result.StandardOutput;
            var error = result.StandardError;

            if (NvccOutputParser.DetectRegisterSpill(output, error, out var errorMessage))
            {
                var suppressGs0001 = false;
                if (File.Exists(sourcePath))
                {
                    try
                    {
                        var content = File.ReadAllText(sourcePath);
                        if (content.Contains("// gsharp:suppress GS0001"))
                        {
                            suppressGs0001 = true;
                        }
                    }
                    catch
                    {
                        // Ignore read errors
                    }
                }

                if (!suppressGs0001)
                {
                    Log.LogWarning(subcategory: null, warningCode: "GS0001", helpKeyword: null, file: sourcePath,
                        lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0, message: errorMessage);
                }
            }

            if (result.ExitCode == 0)
            {
                if (!string.IsNullOrWhiteSpace(error) &&
                    error.IndexOf("ptxas info", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Log.LogMessage(MessageImportance.Low, error);
                }

                return true;
            }

            if (result.ExitCode != 0)
            {
                Log.LogError($"nvcc compilation failed with exit code {result.ExitCode}.");
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
        catch (Exception ex)
        {
            Log.LogError($"Failed to start nvcc at '{NvccPath}': {ex.Message}");
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
        var spillLines = new List<string>();
        var functionContext = "";

        using var reader = new StringReader(error);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.IndexOf("Function properties for", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                functionContext = line.Trim();
            }

            if ((line.IndexOf("ptxas warning", StringComparison.OrdinalIgnoreCase) < 0 ||
                 line.IndexOf("spill", StringComparison.OrdinalIgnoreCase) < 0) &&
                !s_spillRegex.IsMatch(line))
            {
                continue;
            }

            if (line.IndexOf("ptxas warning", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                spillLines.Add(line.Trim());
            }
            else if (!string.IsNullOrEmpty(functionContext))
            {
                spillLines.Add($"{functionContext} -> {line.Trim()}");
            }
            else
            {
                spillLines.Add(line.Trim());
            }
        }

        // Check output string just in case
        using var outReader = new StringReader(output);
        while ((line = outReader.ReadLine()) != null)
        {
            if (s_spillRegex.IsMatch(line))
            {
                spillLines.Add(line.Trim());
            }
        }

        if (spillLines.Count == 0)
        {
            errorMessage = "";
            return false;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Register spill detected during PTX compilation.");
        sb.AppendLine("Details:");
        var distinctSpills = new HashSet<string>();
        foreach (var spill in spillLines)
        {
            if (distinctSpills.Add(spill))
            {
                sb.AppendLine($"  - {spill}");
            }
        }

        sb.Append(
            "Please reduce register pressure (e.g. by avoiding excessive inlining or large stack allocations in GPU kernels).");

        errorMessage = sb.ToString();
        return true;
    }
}
