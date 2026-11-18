using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>
/// MSBuild task that compiles C++ host shadow translation units into object files in parallel and links each
/// native module into a platform dynamic-link library (.dll) via the configured <see cref="INativeToolchain"/>.
/// </summary>
public class CompileCppToNativeTask : CompileKernelTaskBase
{
    /// <summary>The extension appended to an object file path for its incremental build command record.</summary>
    public const string CommandRecordExtension = CppObjectCommandRecord.FileExtension;

    /// <summary>Gets or sets the C++ source files to compile.</summary>
    [Required]
    public ITaskItem[] CppSources { get; set; } = [];

    /// <summary>
    /// Gets or sets the root directory where per-architecture native object files, command records, and per-unit
    /// PDBs are written. Defaults to <c>objs</c> under <see cref="CompileKernelTaskBase.OutputDirectory"/> when unset.
    /// </summary>
    public string ObjectDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the native sanitizers to enable when compiling host C++ kernels (e.g., <c>address;undefined</c>,
    /// <c>address</c>, or <c>undefined</c>). Empty leaves sanitizers disabled.
    /// </summary>
    public string GSharpNativeSanitizers { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether to compile for multiple architectures (x64, arm64).</summary>
    public bool GSharpMultiTarget
    {
        get; set;
    }

    /// <summary>Gets or sets the resulting compiled native DLL task items.</summary>
    [Output]
    public ITaskItem[] OutputDlls { get; set; } = [];

    /// <summary>Gets or sets the per-unit intermediate object, command record, and PDB task items.</summary>
    [Output]
    public ITaskItem[] OutputIntermediateFiles { get; set; } = [];

    private string[] _architectures = [];
    private NativeSanitizers _sanitizers;
    private bool _skipCompilation;

    /// <inheritdoc />
    protected override ITaskItem[] GetSources() => CppSources;

    /// <inheritdoc />
    protected override void SetOutputItems(ITaskItem[] outputItems) => OutputDlls = outputItems;

    /// <inheritdoc />
    protected override ITaskItem[]? ProcessSourceItem(ITaskItem sourceItem) =>
        throw new NotSupportedException("CompileCppToNativeTask uses ExecuteCore for grouped module compilation.");

    /// <inheritdoc />
    protected override bool OnBeforeExecute()
    {
        _architectures = GetTargetArchitectures();

        if (!NativeSanitizerParser.TryParse(GSharpNativeSanitizers, out _sanitizers, out var invalidToken))
        {
            Log.LogError(
                $"Unknown GSharpNativeSanitizers value '{invalidToken}' in '{GSharpNativeSanitizers}'. " +
                "Supported values are 'address' and 'undefined' (separated by ';' or ','), or empty to disable " +
                "sanitizers.");
            return false;
        }

        if ((_sanitizers & NativeSanitizers.Undefined) != 0 && Toolchain is MsvcToolchain)
        {
            Log.LogWarning(MsvcToolchain.UndefinedSanitizerUnsupportedWarning);
        }

        if (MockCompilation)
        {
            return true;
        }

        var prepResult = Toolchain.PrepareEnvironment(_architectures, Log, CancellationToken);
        if (prepResult == ToolchainPreparationResult.SkipCompilation)
        {
            _skipCompilation = true;
            return true;
        }

        return prepResult == ToolchainPreparationResult.Ready;
    }

    /// <inheritdoc />
    protected override bool ExecuteCore()
    {
        CancellationToken.ThrowIfCancellationRequested();

        var sources = GetSources();
        if (sources.Length == 0)
        {
            SetOutputItems([]);
            OutputIntermediateFiles = [];
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
            OutputIntermediateFiles = [];
            return true;
        }

        var pathComparer = Toolchain.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (!TryGroupSourcesByModule(sources, pathComparer, out var modules, out var totalUnits))
        {
            return false;
        }

        if (MockCompilation)
        {
            return ExecuteMockCompilation(modules);
        }

        return ExecuteNativeCompilationAndLink(totalUnits, modules, pathComparer);
    }

    private bool ExecuteMockCompilation(ModuleGroup[] modules)
    {
        var dllItems = new ITaskItem[modules.Length * _architectures.Length];
        var index = 0;

        foreach (var module in modules)
        {
            foreach (var arch in _architectures)
            {
                var outputDllPath = Path.Combine(OutputDirectory, $"{module.ModuleStem}_{arch}.dll");
                File.WriteAllText(outputDllPath, "Mock Native DLL Content");
                dllItems[index++] = CreateDllOutputItem(outputDllPath);
            }
        }

        SetOutputItems(dllItems);
        OutputIntermediateFiles = [];
        return true;
    }

    private bool ExecuteNativeCompilationAndLink(
        int totalUnits,
        ModuleGroup[] modules,
        StringComparer pathComparer)
    {
        var baseObjDir = string.IsNullOrWhiteSpace(ObjectDirectory)
            ? Path.Combine(OutputDirectory, "objs")
            : ObjectDirectory;
        var objExt = Toolchain.IsWindows ? ".obj" : ".o";
        var usePerUnitVcPdb = Toolchain.IsWindows && (_sanitizers & NativeSanitizers.Address) != 0;

        var pendingWorks = new CompileUnitWork[totalUnits * _architectures.Length];
        var pendingCount = 0;
        var linkTargets = new LinkTarget[modules.Length * _architectures.Length];
        var linkIndex = 0;
        var intermediateBag = new ConcurrentBag<ITaskItem>();
        var skippedCount = 0;

        foreach (var arch in _architectures)
        {
            var archObjDir = Path.Combine(baseObjDir, arch);
            Directory.CreateDirectory(archObjDir);
            var compilerIdentity = Toolchain.GetCompilerIdentity(arch, CancellationToken);

            foreach (var module in modules)
            {
                var headerHashes = new Dictionary<string, string>(pathComparer);
                var unitObjPaths = new string[module.Units.Length];

                for (var i = 0; i < module.Units.Length; i++)
                {
                    var unit = module.Units[i];
                    var objPath = Path.Combine(archObjDir, $"{unit.UnitStem}{objExt}");
                    var recordPath = objPath + CppObjectCommandRecord.FileExtension;
                    string? vcPdbPath = usePerUnitVcPdb
                        ? Path.Combine(archObjDir, $"{unit.UnitStem}.vc.pdb")
                        : null;
                    unitObjPaths[i] = objPath;

                    var headerPath = CppObjectCommandRecord.GetModuleHeaderPath(unit.SourcePath, module.ModuleStem);
                    if (!headerHashes.TryGetValue(headerPath, out var headerHash))
                    {
                        headerHash = CppObjectCommandRecord.ComputeFileSha256Hex(
                            headerPath,
                            CppObjectCommandRecord.MissingHeaderSentinel);
                        headerHashes[headerPath] = headerHash;
                    }

                    var compileArgs = Toolchain.BuildCompileObjectArguments(
                        unit.SourcePath, objPath, arch, _sanitizers, vcPdbPath);
                    var unitHash = CppObjectCommandRecord.ComputeFileSha256Hex(
                        unit.SourcePath,
                        CppObjectCommandRecord.MissingUnitSentinel);
                    var record = CppObjectCommandRecord.Build(
                        arch, compilerIdentity, compileArgs, unitHash, headerHash);

                    if (NativeToolchainHelper.HasNonEmptyFile(objPath) &&
                        File.Exists(unit.SourcePath) &&
                        CommandRecordFile.IsCurrent(
                            Log, recordPath, record, CppObjectCommandRecord.RecordDescription))
                    {
                        skippedCount++;
                        AddIntermediateItems(intermediateBag, objPath, recordPath, vcPdbPath);
                        continue;
                    }

                    pendingWorks[pendingCount++] = new CompileUnitWork(
                        unit.SourcePath, objPath, recordPath, vcPdbPath, arch, record);
                }

                linkTargets[linkIndex++] = new LinkTarget(
                    arch,
                    unitObjPaths,
                    Path.Combine(OutputDirectory, $"{module.ModuleStem}_{arch}.dll"));
            }
        }

        if (!CompilePendingUnits(pendingWorks, pendingCount, skippedCount, intermediateBag))
        {
            return false;
        }

        return LinkAndSignModules(linkTargets, intermediateBag);
    }

    private bool CompilePendingUnits(
        CompileUnitWork[] pendingWorks,
        int pendingCount,
        int skippedCount,
        ConcurrentBag<ITaskItem> intermediateBag)
    {
        if (pendingCount == 0)
        {
            Log.LogMessage(
                MessageImportance.High,
                $"Skipping C++ object compilation: all {skippedCount} translation unit(s) are up-to-date.");
            return true;
        }

        var engine9 = BuildEngine as IBuildEngine9;
        var grantedCores = 0;
        var hasErrors = 0;

        try
        {
            var maxConcurrency = 1;
            if (engine9 is not null)
            {
                grantedCores = engine9.RequestCores(pendingCount);
                maxConcurrency = Math.Max(1, grantedCores);
            }

            Log.LogMessage(
                MessageImportance.High,
                $"Compiling {pendingCount} C++ translation unit(s) ({skippedCount} up-to-date) using " +
                $"{maxConcurrency} core(s) (IBuildEngine9={engine9 is not null}, granted={grantedCores}).");

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = CancellationToken
            };

            Parallel.ForEach(new ArraySegment<CompileUnitWork>(pendingWorks, 0, pendingCount), options, work =>
            {
                if (Volatile.Read(ref hasErrors) != 0)
                {
                    return;
                }

                CancellationToken.ThrowIfCancellationRequested();

                if (!CommandRecordFile.TryDelete(Log, work.RecordPath, CppObjectCommandRecord.RecordDescription))
                {
                    Interlocked.Exchange(ref hasErrors, 1);
                    return;
                }

                var sw = Stopwatch.StartNew();
                if (!Toolchain.TryCompileToObject(
                        work.SourcePath,
                        work.ObjPath,
                        work.Arch,
                        Log,
                        CancellationToken,
                        _sanitizers,
                        work.VcPdbPath))
                {
                    Interlocked.Exchange(ref hasErrors, 1);
                    return;
                }

                sw.Stop();
                if (!CommandRecordFile.TryWrite(
                        Log, work.RecordPath, work.RecordContent, CppObjectCommandRecord.RecordDescription))
                {
                    Interlocked.Exchange(ref hasErrors, 1);
                    return;
                }

                Log.LogMessage(
                    MessageImportance.High,
                    $"Compiled {Path.GetFileName(work.SourcePath)} ({work.Arch}) in {sw.ElapsedMilliseconds} ms.");
                AddIntermediateItems(intermediateBag, work.ObjPath, work.RecordPath, work.VcPdbPath);
            });
        }
        finally
        {
            if (engine9 is not null && grantedCores > 0)
            {
                engine9.ReleaseCores(grantedCores);
            }
        }

        return Volatile.Read(ref hasErrors) == 0;
    }

    private bool LinkAndSignModules(LinkTarget[] linkTargets, ConcurrentBag<ITaskItem> intermediateBag)
    {
        var dllItems = new ITaskItem[linkTargets.Length];

        for (var i = 0; i < linkTargets.Length; i++)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var target = linkTargets[i];

            var linkSw = Stopwatch.StartNew();
            if (!Toolchain.TryLinkSharedLibrary(
                    target.UnitObjectPaths,
                    target.OutputDllPath,
                    target.Arch,
                    Log,
                    CancellationToken,
                    _sanitizers))
            {
                return false;
            }

            linkSw.Stop();
            Log.LogMessage(
                MessageImportance.High,
                $"Linked {Path.GetFileName(target.OutputDllPath)} ({target.UnitObjectPaths.Length} unit(s)) " +
                $"in {linkSw.ElapsedMilliseconds} ms.");

            if (!ArtifactHelper.TrySignArtifact(target.OutputDllPath, Log))
            {
                return false;
            }

            dllItems[i] = CreateDllOutputItem(target.OutputDllPath);
        }

        SetOutputItems(dllItems);
        OutputIntermediateFiles = [.. intermediateBag];
        return true;
    }

    private static void AddIntermediateItems(
        ConcurrentBag<ITaskItem> intermediateBag,
        string objPath,
        string recordPath,
        string? vcPdbPath)
    {
        intermediateBag.Add(new TaskItem(objPath));
        intermediateBag.Add(new TaskItem(recordPath));
        if (vcPdbPath is not null && File.Exists(vcPdbPath))
        {
            intermediateBag.Add(new TaskItem(vcPdbPath));
        }
    }

    private static TaskItem CreateDllOutputItem(string outputDllPath)
    {
        var item = new TaskItem(outputDllPath);
        item.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(outputDllPath)}");
        return item;
    }

    private bool TryGroupSourcesByModule(
        ITaskItem[] sources,
        StringComparer pathComparer,
        out ModuleGroup[] modules,
        out int totalUnits)
    {
        var parsed = new (string SourcePath, string ModuleStem, string UnitStem)[sources.Length];
        var parsedCount = 0;
        var seenSources = new HashSet<string>(pathComparer);
        var unitSourcesByStem = new Dictionary<string, string>(pathComparer);
        var moduleIndices = new Dictionary<string, int>(pathComparer);
        var moduleStems = new string[sources.Length];
        var unitCounts = new int[sources.Length];
        var moduleCount = 0;

        for (var i = 0; i < sources.Length; i++)
        {
            var rawPath = sources[i].GetMetadata("FullPath");
            var candidatePath = !string.IsNullOrEmpty(rawPath) ? rawPath : sources[i].ItemSpec;
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                Log.LogError("Invalid C++ source entry: source path cannot be empty.");
                modules = [];
                totalUnits = 0;
                return false;
            }

            var sourcePath = Path.GetFullPath(candidatePath);
            if (!seenSources.Add(sourcePath))
            {
                continue;
            }

            var (moduleStem, unitStem) = ArtifactHelper.GetNativeModuleAndUnitStem(sourcePath);
            if (string.IsNullOrEmpty(moduleStem) || string.IsNullOrEmpty(unitStem))
            {
                Log.LogError($"Invalid C++ source path '{sourcePath}': expected a non-empty file stem.");
                modules = [];
                totalUnits = 0;
                return false;
            }

            if (unitSourcesByStem.TryGetValue(unitStem, out var existingSource))
            {
                Log.LogError(
                    $"Multiple C++ sources resolve to the same object unit stem '{unitStem}': " +
                    $"'{existingSource}' and '{sourcePath}'.");
                modules = [];
                totalUnits = 0;
                return false;
            }

            unitSourcesByStem[unitStem] = sourcePath;
            parsed[parsedCount++] = (sourcePath, moduleStem, unitStem);

            if (!moduleIndices.TryGetValue(moduleStem, out var moduleIndex))
            {
                moduleIndex = moduleCount++;
                moduleIndices[moduleStem] = moduleIndex;
                moduleStems[moduleIndex] = moduleStem;
            }

            unitCounts[moduleIndex]++;
        }

        modules = new ModuleGroup[moduleCount];
        for (var i = 0; i < moduleCount; i++)
        {
            modules[i] = new ModuleGroup(moduleStems[i], new UnitSource[unitCounts[i]]);
            unitCounts[i] = 0;
        }

        for (var i = 0; i < parsedCount; i++)
        {
            var entry = parsed[i];
            var moduleIndex = moduleIndices[entry.ModuleStem];
            modules[moduleIndex].Units[unitCounts[moduleIndex]++] = new UnitSource(entry.SourcePath, entry.UnitStem);
        }

        totalUnits = parsedCount;
        return true;
    }

    /// <summary>Gets the architecture name (<c>x64</c> or <c>arm64</c>) of the process that runs the build.</summary>
    internal static string HostArchitecture =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    private string[] GetTargetArchitectures() => GSharpMultiTarget ? ["x64", "arm64"] : [HostArchitecture];

    private readonly record struct UnitSource(string SourcePath, string UnitStem);

    private readonly record struct ModuleGroup(string ModuleStem, UnitSource[] Units);

    private readonly record struct CompileUnitWork(
        string SourcePath,
        string ObjPath,
        string RecordPath,
        string? VcPdbPath,
        string Arch,
        string RecordContent);

    private readonly record struct LinkTarget(
        string Arch,
        string[] UnitObjectPaths,
        string OutputDllPath);
}
