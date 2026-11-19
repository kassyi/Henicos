using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>MSBuild task that compiles C++ host shadow implementations into native platform dynamic-link libraries (.dll) via the configured <see cref="INativeToolchain"/>.</summary>
public class CompileCppToNativeTask : CompileKernelTaskBase
{
    /// <summary>Gets or sets the C++ source files to compile.</summary>
    [Required]
    public ITaskItem[] CppSources { get; set; } = [];

    /// <summary>
    /// Gets or sets the native sanitizers to enable when compiling host C++ kernels (e.g., <c>address;undefined</c>,
    /// <c>address</c>, or <c>undefined</c>). Empty leaves sanitizers disabled.
    /// </summary>
    public string GSharpNativeSanitizers { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether to compile for multiple architectures (x64 and arm64).</summary>
    public bool GSharpMultiTarget
    {
        get; set;
    }

    /// <summary>Gets or sets the resulting compiled native DLL task items.</summary>
    /// <remarks>
    /// Each item compiled by the toolchain carries the <c>CxxCommandFile</c> metadata: the path of the file that records the
    /// command the library was built with (see <see cref="CommandFileExtension"/>).
    /// </remarks>
    [Output]
    public ITaskItem[] OutputDlls { get; set; } = [];

    /// <summary>
    /// The extension appended to a library path for the file that records the compiler, its arguments and the signing key
    /// the library was built with. A library whose record differs from the current command is rebuilt even when it is
    /// newer than its sources; the headers that the sources include are not recorded (the generated sources include only
    /// system headers).
    /// </summary>
    public const string CommandFileExtension = ".cxx";

    private const string CxxTool = "C++ compiler";

    private string[] _architectures = [];
    private NativeSanitizers _sanitizers;
    private bool _skipCompilation;

    /// <inheritdoc />
    protected override ITaskItem[] GetSources() => CppSources;

    /// <inheritdoc />
    protected override void SetOutputItems(ITaskItem[] outputItems) => OutputDlls = outputItems;

    /// <inheritdoc />
    protected override bool OnBeforeExecute()
    {
        _architectures = GetTargetArchitectures();

        if (!NativeSanitizerParser.TryParse(GSharpNativeSanitizers, out _sanitizers, out var invalidToken))
        {
            Log.LogError(
                $"Unknown GSharpNativeSanitizers value '{invalidToken}' in '{GSharpNativeSanitizers}'. Supported values are 'address' and 'undefined' (separated by ';' or ','), or empty to disable sanitizers.");
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

    protected override ITaskItem[]? ProcessSourceItem(ITaskItem sourceItem)
    {
        if (_skipCompilation)
        {
            return [];
        }

        var sourcePath = sourceItem.GetMetadata("FullPath");
        var fileName = ArtifactHelper.GetCleanKernelFileName(sourcePath);
        var items = new ConcurrentBag<ITaskItem>();

        var hasErrorsBox = new[] { 0 };

        var options = new ParallelOptions
        {
            CancellationToken = CancellationToken
        };

        Parallel.ForEach(_architectures, options, arch =>
        {
            if (Volatile.Read(ref hasErrorsBox[0]) != 0)
            {
                return;
            }

            var outputDllPath = Path.Combine(OutputDirectory, $"{fileName}_{arch}.dll");

            if (MockCompilation)
            {
                File.WriteAllText(outputDllPath, "Mock Native DLL Content");
                items.Add(CreateOutputItem(outputDllPath, commandFilePath: null));

                return;
            }

            var commandRecord = Toolchain.DescribeCompilation(sourcePath, outputDllPath, arch, _sanitizers) +
                "\nsigning-key " + ArtifactHelper.GetSigningKeyFingerprint();
            var commandFilePath = outputDllPath + CommandFileExtension;
            if (ArtifactHelper.IsNewerThanSources(outputDllPath, [sourcePath]) &&
                CommandRecordFile.IsCurrent(Log, CxxTool, commandFilePath, commandRecord))
            {
                Log.LogMessage(MessageImportance.Low,
                    $"Skipping compilation because '{outputDllPath}' is up-to-date.");
                items.Add(CreateOutputItem(outputDllPath, commandFilePath));
                return;
            }

            if (!CommandRecordFile.TryDelete(Log, CxxTool, commandFilePath) ||
                !Toolchain.TryCompileCppToSharedLibrary(
                    sourcePath, outputDllPath, arch, Log, CancellationToken, _sanitizers) ||
                !ArtifactHelper.TrySignArtifact(outputDllPath, Log) ||
                !CommandRecordFile.TryWrite(Log, CxxTool, commandFilePath, commandRecord))
            {
                Interlocked.Exchange(ref hasErrorsBox[0], 1);
                return;
            }

            items.Add(CreateOutputItem(outputDllPath, commandFilePath));
        });

        return Volatile.Read(ref hasErrorsBox[0]) != 0 ? null : [.. items];
    }

    private static TaskItem CreateOutputItem(string dllPath, string? commandFilePath)
    {
        var item = new TaskItem(dllPath);
        item.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(dllPath)}");
        if (commandFilePath is not null)
        {
            item.SetMetadata("CxxCommandFile", commandFilePath);
        }

        return item;
    }

    /// <summary>Gets the architecture name (<c>x64</c> or <c>arm64</c>) of the process that runs the build.</summary>
    internal static string HostArchitecture =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    private string[] GetTargetArchitectures() => GSharpMultiTarget ? ["x64", "arm64"] : [HostArchitecture];
}
