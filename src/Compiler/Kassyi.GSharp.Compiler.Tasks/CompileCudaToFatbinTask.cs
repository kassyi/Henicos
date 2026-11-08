using System.Text;

namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>MSBuild task that compiles CUDA C++ (.cu) kernel sources into GPU fatbin binaries using NVCC.</summary>
public class CompileCudaToFatbinTask : CompileKernelTaskBase
{
    /// <summary>Gets or sets the CUDA (.cu) source files to compile.</summary>
    [Required]
    public ITaskItem[] CudaSources { get; set; } = [];

    /// <summary>
    /// The architectures compiled when <see cref="TargetArchitectures"/> is not set (an empty
    /// <c>GSharpTargetArchitectures</c> MSBuild property passes nothing): sm_86 SASS, and compute_86 PTX that the driver
    /// JIT-compiles on GPUs the SASS cannot run on (compute capability 9.0 and newer). Both are generated from the same
    /// compute_86 PTX, so the PTX costs only its space in the fatbin. 7.5 and 8.0 GPUs need their own entries.
    /// </summary>
    public const string DefaultTargetArchitectures = "compute_86,sm_86;compute_86,compute_86";

    /// <summary>
    /// Gets or sets the target GPU architectures as <c>arch,code</c> pairs separated by <c>;</c>; each pair becomes
    /// <c>-gencode arch=&lt;arch&gt;,code=&lt;code&gt;</c> (e.g. <c>compute_86,sm_86;compute_86,compute_86</c>).
    /// </summary>
    public string TargetArchitectures { get; set; } = DefaultTargetArchitectures;

    /// <summary>Gets or sets the path or executable name for the NVIDIA CUDA compiler (nvcc).</summary>
    public string NvccPath { get; set; } = "nvcc";

    /// <summary>
    /// When specified, bundles all <see cref="CudaSources"/> into a single fatbin with this file name.
    /// </summary>
    public string? CombinedFatbinName
    {
        get; set;
    }

    /// <summary>Gets or sets the resulting compiled fatbin task items.</summary>
    /// <remarks>
    /// Each item compiled by nvcc carries the <c>NvccCommandFile</c> metadata: the path of the file that records the nvcc
    /// command the fatbin was built with (see <see cref="CommandFileExtension"/>).
    /// </remarks>
    [Output]
    public ITaskItem[] OutputFatbins { get; set; } = [];

    /// <summary>
    /// The extension appended to a fatbin path for the file that records the nvcc options, sources and version and the
    /// signing key the fatbin was built with. A fatbin whose record differs from the current command is rebuilt even when
    /// it is newer than its sources. The <c>NVCC_PREPEND_FLAGS</c> / <c>NVCC_APPEND_FLAGS</c> environment variables, the
    /// host C++ compiler version and the included headers are not recorded.
    /// </summary>
    public const string CommandFileExtension = ".nvcc";

    private string _nvccOptions = "";
    private string _nvccVersion = "";
    private bool _skipCompilation;

    /// <inheritdoc />
    protected override ITaskItem[] GetSources() => CudaSources;

    /// <inheritdoc />
    protected override void SetOutputItems(ITaskItem[] outputItems) => OutputFatbins = outputItems;

    /// <inheritdoc />
    protected override bool ExecuteCore() =>
        string.IsNullOrWhiteSpace(CombinedFatbinName) ? base.ExecuteCore() : ExecuteCombinedCompilation();

    private bool ExecuteCombinedCompilation()
    {
        var sources = GetSources();
        if (sources.Length == 0)
        {
            SetOutputItems([]);
            return true;
        }

        if (!Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }

        if (!OnBeforeExecute())
        {
            return false;
        }

        if (_skipCompilation)
        {
            SetOutputItems([]);
            return true;
        }

        return CompileCombined(sources);
    }

    private bool CompileCombined(ITaskItem[] sources)
    {
        var outputName = Path.ChangeExtension(CombinedFatbinName, ".gfatbin") ?? $"{CombinedFatbinName}.gfatbin";
        var outputFatbinPath = Path.Combine(OutputDirectory, outputName);

        if (MockCompilation)
        {
            File.WriteAllText(outputFatbinPath, "Mock Fatbin Content");
            SetOutputItems([CreateOutputItem(outputFatbinPath, commandFilePath: null)]);
            return true;
        }

        var sourcePaths = new string[sources.Length];
        for (var i = 0; i < sources.Length; i++)
        {
            sourcePaths[i] = sources[i].GetMetadata("FullPath");
        }

        var commandRecord = NvccCommandRecord.Build(_nvccOptions, _nvccVersion, sourcePaths);
        var commandFilePath = outputFatbinPath + CommandFileExtension;
        if (IsNewerThanSources(outputFatbinPath, sourcePaths) &&
            NvccCommandRecord.IsCurrent(Log, commandFilePath, commandRecord))
        {
            Log.LogMessage(MessageImportance.Low,
                $"Skipping compilation because '{outputFatbinPath}' is up-to-date.");
            SetOutputItems([CreateOutputItem(outputFatbinPath, commandFilePath)]);
            return true;
        }

        if (!BuildCombinedFatbin(sourcePaths, outputFatbinPath, commandFilePath, commandRecord))
        {
            return false;
        }

        SetOutputItems([CreateOutputItem(outputFatbinPath, commandFilePath)]);
        return true;
    }

    // Writes the bundle that includes every source, compiles it, signs the fatbin and records the command.
    private bool BuildCombinedFatbin(
        string[] sourcePaths,
        string outputFatbinPath,
        string commandFilePath,
        string commandRecord)
    {
        var bundleFileName = Path.GetFileNameWithoutExtension(CombinedFatbinName) + "_bundle.cu";
        var bundleFilePath = Path.Combine(OutputDirectory, bundleFileName);
        var bundledPaths = new List<string>();

        try
        {
            File.WriteAllText(bundleFilePath, BuildBundleContent(sourcePaths, bundleFileName, bundledPaths));
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to write bundle file '{bundleFilePath}': {ex.Message}");
            return false;
        }

        if (!NvccCommandRecord.Delete(Log, commandFilePath) ||
            !CompileCombinedWithNvcc([bundleFilePath], outputFatbinPath, bundledPaths))
        {
            return false;
        }

        return ArtifactHelper.ProcessArtifact(outputFatbinPath, Log) &&
            NvccCommandRecord.Write(Log, commandFilePath, commandRecord);
    }

    // One #include per source, except an earlier bundle of the same name. The included paths are added to bundledPaths.
    private static string BuildBundleContent(
        IReadOnlyList<string> sourcePaths,
        string bundleFileName,
        List<string> bundledPaths)
    {
        var bundleContent = new StringBuilder();
        foreach (var sp in sourcePaths)
        {
            if (string.Equals(Path.GetFileName(sp), bundleFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bundledPaths.Add(sp);
            bundleContent.Append("#include \"");
            foreach (var c in sp)
            {
                bundleContent.Append(c == '\\' ? '/' : c);
            }

            bundleContent.AppendLine("\"");
        }

        return bundleContent.ToString();
    }

    // Returns false when the fatbin does not exist. A source that does not exist is skipped, as before; removing a source
    // still changes the nvcc command record.
    private static bool IsNewerThanSources(string fatbinPath, IReadOnlyList<string> sourcePaths)
    {
        if (!File.Exists(fatbinPath))
        {
            return false;
        }

        var fatbinTime = File.GetLastWriteTimeUtc(fatbinPath);
        foreach (var sourcePath in sourcePaths)
        {
            if (File.Exists(sourcePath) && File.GetLastWriteTimeUtc(sourcePath) > fatbinTime)
            {
                return false;
            }
        }

        return true;
    }

    private static TaskItem CreateOutputItem(string fatbinPath, string? commandFilePath)
    {
        var item = new TaskItem(fatbinPath);
        item.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(fatbinPath)}");
        if (commandFilePath is not null)
        {
            item.SetMetadata("NvccCommandFile", commandFilePath);
        }

        return item;
    }

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

        _nvccOptions = NvccInvoker.BuildOptions(SplitSemicolon(TargetArchitectures), Toolchain.IsWindows);
        if (!Toolchain.IsWindows)
        {
            return true;
        }

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
            var result = this.ProcessRunner.Run(checkInfo, CancellationToken);
            if (result.ExitCode != 0)
            {
                return false;
            }

            // The last line names the build ("Build cuda_13.4.r13.4/compiler.xxx"); it goes into the command record.
            _nvccVersion = GetLastLine(result.StandardOutput);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
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
        var outputFatbinPath = Path.Combine(OutputDirectory, $"{fileName}.gfatbin");

        if (MockCompilation)
        {
            File.WriteAllText(outputFatbinPath, "Mock Fatbin Content");
            return [CreateOutputItem(outputFatbinPath, commandFilePath: null)];
        }

        var commandRecord = NvccCommandRecord.Build(_nvccOptions, _nvccVersion, [sourcePath]);
        var commandFilePath = outputFatbinPath + CommandFileExtension;
        if (File.Exists(sourcePath) && IsNewerThanSources(outputFatbinPath, [sourcePath]) &&
            NvccCommandRecord.IsCurrent(Log, commandFilePath, commandRecord))
        {
            Log.LogMessage(MessageImportance.Low,
                $"Skipping compilation of '{sourcePath}' because '{outputFatbinPath}' is up-to-date.");
            return [CreateOutputItem(outputFatbinPath, commandFilePath)];
        }

        if (!NvccCommandRecord.Delete(Log, commandFilePath) || !CompileWithNvcc(sourcePath, outputFatbinPath))
        {
            return null;
        }

        if (!ArtifactHelper.ProcessArtifact(outputFatbinPath, Log) ||
            !NvccCommandRecord.Write(Log, commandFilePath, commandRecord))
        {
            return null;
        }

        return [CreateOutputItem(outputFatbinPath, commandFilePath)];
    }

    private bool CompileWithNvcc(string sourcePath, string outputFatbinPath)
    {
        var args = NvccInvoker.BuildArguments(_nvccOptions, [sourcePath], outputFatbinPath);
        Log.LogMessage(MessageImportance.Normal, $"Compiling CUDA source: {sourcePath}");

        if (!RunNvcc([sourcePath], args))
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

    private bool RunNvcc(IReadOnlyList<string> sourcePaths, string arguments) =>
        NvccInvoker.Run(Log, ProcessRunner, NvccPath, _vcvarsEnv, sourcePaths, arguments, CancellationToken);

    private bool CompileCombinedWithNvcc(IReadOnlyList<string> sourcePaths, string outputFatbinPath,
        IReadOnlyList<string> originalSourcePaths)
    {
        var args = NvccInvoker.BuildArguments(_nvccOptions, sourcePaths, outputFatbinPath);
        Log.LogMessage(MessageImportance.Normal,
            $"Compiling {originalSourcePaths.Count} CUDA sources into combined fatbin: {outputFatbinPath}");

        if (!RunNvcc(originalSourcePaths, args))
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

    private static string GetLastLine(string text)
    {
        var trimmed = text.AsSpan().TrimEnd();
        return trimmed.Slice(trimmed.LastIndexOf('\n') + 1).Trim().ToString();
    }

    private static string[] SplitSemicolon(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        var list = new List<string>();
        var span = value.AsSpan();
        while (span.Length > 0)
        {
            var idx = span.IndexOf(';');
            var segment = idx >= 0 ? span.Slice(0, idx) : span;
            var trimmed = segment.Trim();
            if (trimmed.Length > 0)
            {
                list.Add(trimmed.ToString());
            }

            if (idx < 0)
            {
                break;
            }

            span = span.Slice(idx + 1);
        }

        return [.. list];
    }
}
