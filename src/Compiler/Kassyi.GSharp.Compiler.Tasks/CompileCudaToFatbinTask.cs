using System.Text;

namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>MSBuild task that compiles CUDA C++ (.cu) kernel sources into GPU fatbin binaries using NVCC.</summary>
public class CompileCudaToFatbinTask : CompileKernelTaskBase
{
    /// <summary>Gets or sets the CUDA (.cu) source files to compile.</summary>
    [Required]
    public ITaskItem[] CudaSources { get; set; } = [];

    /// <summary>Gets or sets the target GPU architectures (e.g. compute_86,sm_86).</summary>
    public string TargetArchitectures
    {
        get; set;
    } =
        "compute_86,sm_86";

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

        var commandRecord = BuildCommandRecord(sourcePaths);
        var commandFilePath = outputFatbinPath + CommandFileExtension;
        if (IsNewerThanSources(outputFatbinPath, sourcePaths) && IsCommandRecordCurrent(commandFilePath, commandRecord))
        {
            Log.LogMessage(MessageImportance.Low,
                $"Skipping compilation because '{outputFatbinPath}' is up-to-date.");
            SetOutputItems([CreateOutputItem(outputFatbinPath, commandFilePath)]);
            return true;
        }

        var bundleFileName = Path.GetFileNameWithoutExtension(CombinedFatbinName) + "_bundle.cu";
        var bundleFilePath = Path.Combine(OutputDirectory, bundleFileName);

        var bundleContent = new StringBuilder();
        var originalPathsFiltered = new List<string>();
        foreach (var sp in sourcePaths)
        {
            if (string.Equals(Path.GetFileName(sp), bundleFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            originalPathsFiltered.Add(sp);
            bundleContent.Append("#include \"");
            foreach (var c in sp)
            {
                bundleContent.Append(c == '\\' ? '/' : c);
            }
            bundleContent.AppendLine("\"");
        }

        try
        {
            File.WriteAllText(bundleFilePath, bundleContent.ToString());
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to write bundle file '{bundleFilePath}': {ex.Message}");
            return false;
        }

        if (!DeleteCommandRecord(commandFilePath) ||
            !CompileCombinedWithNvcc([bundleFilePath], outputFatbinPath, originalPathsFiltered))
        {
            return false;
        }

        if (!ArtifactHelper.ProcessArtifact(outputFatbinPath, Log) || !WriteCommandRecord(commandFilePath, commandRecord))
        {
            return false;
        }

        SetOutputItems([CreateOutputItem(outputFatbinPath, commandFilePath)]);
        return true;
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

    // [WHY] Timestamps alone miss a change of the nvcc command: a new default or GSharpTargetArchitectures value, another
    // nvcc, a new signing key, or a source added to a combined fatbin while older than the fatbin. Like MSBuild's
    // CoreCompileInputs.cache, the record holds what decides the output except the output path:
    // <options>\n<source>\n...signing-key <public key>\n<last line of nvcc --version>
    // Not recorded (a change needs a rebuild by hand, as in a C++ build without dependency files): the
    // NVCC_PREPEND_FLAGS / NVCC_APPEND_FLAGS environment variables, the version of the host C++ compiler, and the headers
    // that the sources include.
    private string BuildCommandRecord(IReadOnlyList<string> sourcePaths)
    {
        var record = new StringBuilder();
        record.Append(_nvccOptions).Append('\n');
        foreach (var sourcePath in sourcePaths)
        {
            record.Append(sourcePath).Append('\n');
        }

        record.Append("signing-key ").Append(ArtifactHelper.GetSigningKeyFingerprint()).Append('\n');
        record.Append(_nvccVersion);
        return record.ToString();
    }

    // Removes the record before nvcc runs: when nvcc, signing or the record write fails, no record of an earlier command
    // can match the fatbin left behind, so the next build compiles again
    private bool DeleteCommandRecord(string commandFilePath)
    {
        try
        {
            if (File.Exists(commandFilePath))
            {
                File.Delete(commandFilePath);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.LogError($"Failed to delete the nvcc command record '{commandFilePath}': {ex.Message}");
            return false;
        }
    }

    private bool IsCommandRecordCurrent(string commandFilePath, string commandRecord)
    {
        if (!File.Exists(commandFilePath))
        {
            Log.LogMessage(MessageImportance.Low, $"Recompiling because '{commandFilePath}' does not exist.");
            return false;
        }

        try
        {
            if (string.Equals(File.ReadAllText(commandFilePath), commandRecord, StringComparison.Ordinal))
            {
                return true;
            }

            Log.LogMessage(MessageImportance.Normal,
                $"Recompiling because the nvcc command differs from the one recorded in '{commandFilePath}'.");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.LogMessage(MessageImportance.Low,
                $"Recompiling because '{commandFilePath}' could not be read: {ex.Message}");
            return false;
        }
    }

    private bool WriteCommandRecord(string commandFilePath, string commandRecord)
    {
        try
        {
            File.WriteAllText(commandFilePath, commandRecord);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.LogError($"Failed to write the nvcc command record '{commandFilePath}': {ex.Message}");
            return false;
        }
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

        _nvccOptions = BuildNvccOptions(SplitSemicolon(TargetArchitectures));
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

        var commandRecord = BuildCommandRecord([sourcePath]);
        var commandFilePath = outputFatbinPath + CommandFileExtension;
        if (File.Exists(sourcePath) && IsNewerThanSources(outputFatbinPath, [sourcePath]) &&
            IsCommandRecordCurrent(commandFilePath, commandRecord))
        {
            Log.LogMessage(MessageImportance.Low,
                $"Skipping compilation of '{sourcePath}' because '{outputFatbinPath}' is up-to-date.");
            return [CreateOutputItem(outputFatbinPath, commandFilePath)];
        }

        if (!DeleteCommandRecord(commandFilePath) || !CompileWithNvcc(sourcePath, outputFatbinPath))
        {
            return null;
        }

        if (!ArtifactHelper.ProcessArtifact(outputFatbinPath, Log) || !WriteCommandRecord(commandFilePath, commandRecord))
        {
            return null;
        }

        return [CreateOutputItem(outputFatbinPath, commandFilePath)];
    }

    private bool CompileWithNvcc(string sourcePath, string outputFatbinPath)
    {
        var args = BuildNvccArguments([sourcePath], outputFatbinPath);
        Log.LogMessage(MessageImportance.Normal, $"Compiling CUDA source: {sourcePath}");

        if (!RunNvccProcess([sourcePath], args))
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

    private bool CompileCombinedWithNvcc(IReadOnlyList<string> sourcePaths, string outputFatbinPath,
        IReadOnlyList<string> originalSourcePaths)
    {
        var args = BuildNvccArguments(sourcePaths, outputFatbinPath);
        Log.LogMessage(MessageImportance.Normal,
            $"Compiling {originalSourcePaths.Count} CUDA sources into combined fatbin: {outputFatbinPath}");

        if (!RunNvccProcess(originalSourcePaths, args))
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

    // Everything on the nvcc command line except the output and the sources.
    private string BuildNvccOptions(string[] architectures)
    {
        var args = new StringBuilder();
        args.Append("--fatbin ");
        args.Append("-std=c++17 ");
        if (Toolchain.IsWindows)
        {
            // /Zc:preprocessor = conforming preprocessor; CCCL's _CCCL_PP_CAT cannot expand under cl.exe's traditional one
            // (thrust headers fail with 'expected a "{"'). Silencing the CCCL #error instead leaves that failure in place.
            // /wd4819 = Suppress the code page warning for non-ASCII characters in headers
            args.Append("-Xcompiler \"/Zc:preprocessor /wd4819\" ");
        }

        args.Append("--ptxas-options=-v,--warn-on-spills ");
        foreach (var arch in architectures)
        {
            var commaIdx = arch.IndexOf(',');
            if (commaIdx > 0)
            {
                args.Append($"-gencode arch={arch[..commaIdx]},code={arch[(commaIdx + 1)..]} ");
            }
        }

        return args.ToString();
    }

    private string BuildNvccArguments(IReadOnlyList<string> sourcePaths, string outputFatbinPath)
    {
        var args = new StringBuilder(_nvccOptions);
        args.Append($"-o \"{outputFatbinPath}\" ");
        for (var i = 0; i < sourcePaths.Count; i++)
        {
            if (i > 0)
            {
                args.Append(' ');
            }

            args.Append($"\"{sourcePaths[i]}\"");
        }

        return args.ToString();
    }

    private bool RunNvccProcess(IReadOnlyList<string> sourcePaths, string arguments)
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
            var result = this.ProcessRunner.Run(startInfo, CancellationToken);
            var output = result.StandardOutput;
            var error = result.StandardError;

            if (NvccOutputParser.DetectRegisterSpill(output, error, out var errorMessage))
            {
                var suppressGs0001 = false;
                foreach (var sourcePath in sourcePaths)
                {
                    if (!File.Exists(sourcePath))
                    {
                        continue;
                    }

                    try
                    {
                        var content = File.ReadAllText(sourcePath);
                        if (!content.Contains("// gsharp:suppress GS0001"))
                        {
                            continue;
                        }

                        suppressGs0001 = true;
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Log.LogMessage(MessageImportance.Low,
                            $"Could not read '{sourcePath}' to look for '// gsharp:suppress GS0001': {ex.Message}");
                    }
                }

                if (!suppressGs0001)
                {
                    var reportPath = sourcePaths.Count > 0 ? sourcePaths[0] : "";
                    Log.LogWarning(subcategory: null, warningCode: "GS0001", helpKeyword: null, file: reportPath,
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

            RenamedIdentifierLogger.LogRenamedIdentifiers(Log, output, error);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to start nvcc at '{NvccPath}': {ex.Message}");
            return false;
        }
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

internal static class NvccOutputParser
{
    private static bool HasSpill(string line)
    {
        const string Target = "bytes spill";
        var index = 0;
        while (index < line.Length)
        {
            var found = line.IndexOf(Target, index, StringComparison.OrdinalIgnoreCase);
            if (found <= 0)
            {
                return false;
            }

            var i = found - 1;
            while (i >= 0 && char.IsWhiteSpace(line[i]))
            {
                i--;
            }

            var endOfDigits = i;
            while (i >= 0 && char.IsDigit(line[i]))
            {
                i--;
            }

            var startOfDigits = i + 1;
            if (startOfDigits <= endOfDigits && line[startOfDigits] != '0')
            {
                return true;
            }

            index = found + Target.Length;
        }

        return false;
    }

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
                functionContext = line.AsSpan().Trim().ToString();
            }

            if ((line.IndexOf("ptxas warning", StringComparison.OrdinalIgnoreCase) < 0 ||
                 line.IndexOf("spill", StringComparison.OrdinalIgnoreCase) < 0) &&
                !HasSpill(line))
            {
                continue;
            }

            if (line.IndexOf("ptxas warning", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                spillLines.Add(line.AsSpan().Trim().ToString());
            }
            else if (!string.IsNullOrEmpty(functionContext))
            {
                spillLines.Add($"{functionContext} -> {line.AsSpan().Trim().ToString()}");
            }
            else
            {
                spillLines.Add(line.AsSpan().Trim().ToString());
            }
        }

        // Check output string just in case
        using var outReader = new StringReader(output);
        while ((line = outReader.ReadLine()) != null)
        {
            if (HasSpill(line))
            {
                spillLines.Add(line.AsSpan().Trim().ToString());
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
