using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>MSBuild task that compiles C++ host shadow implementations into native platform dynamic-link libraries (.dll) via the configured <see cref="INativeToolchain"/>.</summary>
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
    private bool _skipCompilation;

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
                var mockTaskItem = new TaskItem(outputDllPath);
                mockTaskItem.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(outputDllPath)}");
                items.Add(mockTaskItem);

                return;
            }

            if (!Toolchain.CompileCppToSharedLibrary(sourcePath, outputDllPath, arch, Log, CancellationToken) ||
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
}
