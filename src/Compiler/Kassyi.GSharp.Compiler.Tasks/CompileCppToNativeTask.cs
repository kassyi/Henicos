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
        var modules = NativeModuleGrouper.Group(sources, pathComparer, Log);
        if (modules is null)
        {
            return false;
        }

        if (MockCompilation)
        {
            return ExecuteMockCompilation(modules);
        }

        return ExecuteNativeCompilationAndLink(modules, pathComparer);
    }

    private bool ExecuteMockCompilation(NativeModuleGroup[] modules)
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

    private bool ExecuteNativeCompilationAndLink(NativeModuleGroup[] modules, StringComparer pathComparer)
    {
        var settings = new NativeUnitBuildSettings(
            string.IsNullOrWhiteSpace(ObjectDirectory) ? Path.Combine(OutputDirectory, "objs") : ObjectDirectory,
            OutputDirectory,
            Toolchain.IsWindows ? ".obj" : ".o",
            Toolchain.IsWindows && (_sanitizers & NativeSanitizers.Address) != 0,
            _sanitizers,
            pathComparer);
        var plan = NativeUnitBuildPlanner.Plan(modules, _architectures, settings, Toolchain, Log, CancellationToken);

        var intermediateBag = new ConcurrentBag<ITaskItem>();
        foreach (var artifacts in plan.UpToDateUnits)
        {
            AddIntermediateItems(intermediateBag, artifacts);
        }

        if (!CompilePendingUnits(plan, intermediateBag))
        {
            return false;
        }

        return LinkAndSignModules(plan.LinkTargets, intermediateBag);
    }

    private bool CompilePendingUnits(NativeUnitBuildPlan plan, ConcurrentBag<ITaskItem> intermediateBag)
    {
        var pendingCount = plan.PendingUnits.Length;
        var skippedCount = plan.UpToDateUnits.Length;
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

            Parallel.ForEach(plan.PendingUnits, options, work =>
            {
                if (Volatile.Read(ref hasErrors) != 0)
                {
                    return;
                }

                if (!CompileUnit(work, intermediateBag))
                {
                    Interlocked.Exchange(ref hasErrors, 1);
                }
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

    private bool CompileUnit(NativeUnitCompileWork work, ConcurrentBag<ITaskItem> intermediateBag)
    {
        CancellationToken.ThrowIfCancellationRequested();

        var artifacts = work.Artifacts;
        if (!CommandRecordFile.TryDelete(Log, artifacts.RecordPath, CppObjectCommandRecord.RecordDescription))
        {
            return false;
        }

        var sw = Stopwatch.StartNew();
        if (!Toolchain.TryCompileToObject(
                work.SourcePath,
                artifacts.ObjPath,
                work.Arch,
                Log,
                CancellationToken,
                _sanitizers,
                artifacts.VcPdbPath))
        {
            return false;
        }

        sw.Stop();
        if (!CommandRecordFile.TryWrite(
                Log, artifacts.RecordPath, work.RecordContent, CppObjectCommandRecord.RecordDescription))
        {
            return false;
        }

        Log.LogMessage(
            MessageImportance.High,
            $"Compiled {Path.GetFileName(work.SourcePath)} ({work.Arch}) in {sw.ElapsedMilliseconds} ms.");
        AddIntermediateItems(intermediateBag, artifacts);
        return true;
    }

    private bool LinkAndSignModules(NativeLinkTarget[] linkTargets, ConcurrentBag<ITaskItem> intermediateBag)
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

    private static void AddIntermediateItems(ConcurrentBag<ITaskItem> intermediateBag, NativeUnitArtifacts artifacts)
    {
        intermediateBag.Add(new TaskItem(artifacts.ObjPath));
        intermediateBag.Add(new TaskItem(artifacts.RecordPath));
        if (artifacts.VcPdbPath is not null && File.Exists(artifacts.VcPdbPath))
        {
            intermediateBag.Add(new TaskItem(artifacts.VcPdbPath));
        }
    }

    private static TaskItem CreateDllOutputItem(string outputDllPath)
    {
        var item = new TaskItem(outputDllPath);
        item.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(outputDllPath)}");
        return item;
    }

    /// <summary>Gets the architecture name (<c>x64</c> or <c>arm64</c>) of the process that runs the build.</summary>
    internal static string HostArchitecture =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    private string[] GetTargetArchitectures() => GSharpMultiTarget ? ["x64", "arm64"] : [HostArchitecture];
}
