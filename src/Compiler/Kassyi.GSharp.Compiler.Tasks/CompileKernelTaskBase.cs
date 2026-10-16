using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>Base MSBuild task providing parallel compilation, native toolchain resolution, and artifact dispatching for kernel sources.</summary>
public abstract class CompileKernelTaskBase : Microsoft.Build.Utilities.Task, ICancelableTask
{
    private readonly CancellationTokenSource _cancellation = new();
    private INativeToolchain? _toolchain;

    /// <summary>Gets the cancellation token for the current task execution.</summary>
    protected CancellationToken CancellationToken => _cancellation.Token;

    /// <inheritdoc />
    public void Cancel() => _cancellation.Cancel();

    /// <summary>Gets or sets the target directory where compiled artifacts are placed.</summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether to generate mock compilation outputs during testing.</summary>
    public bool MockCompilation
    {
        get; set;
    }

    /// <summary>Gets or sets the process runner to use for executing external tools.</summary>
    public IProcessRunner ProcessRunner { get; set; } = Helpers.ProcessRunner.Default;

    /// <summary>
    /// Gets or sets the native C++ toolchain used for environment discovery and host compilation.
    /// Defaults to <see cref="MsvcToolchain"/> on Windows and <see cref="GccClangToolchain"/> on non-Windows platforms.
    /// </summary>
    public INativeToolchain Toolchain
    {
        get => _toolchain ??= CreateDefaultToolchain();
        set => _toolchain = value;
    }

    private INativeToolchain CreateDefaultToolchain() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new MsvcToolchain(ProcessRunner, DisableVcvarsCache)
            : new GccClangToolchain(ProcessRunner);

    /// <summary>Retrieves source items to be processed by this compilation task.</summary>
    protected abstract ITaskItem[] GetSources();

    /// <summary>Compiles or transforms an individual source item into output artifacts.</summary>
    protected abstract ITaskItem[]? ProcessSourceItem(ITaskItem sourceItem);

    /// <summary>Sets the output task items produced by compilation.</summary>
    protected abstract void SetOutputItems(ITaskItem[] outputItems);

    /// <inheritdoc />
    public override bool Execute()
    {
        try
        {
            return ExecuteCore();
        }
        catch (OperationCanceledException)
        {
            Log.LogWarning("Task execution was canceled.");
            return false;
        }
        catch (AggregateException ex) when (IsCanceled(ex))
        {
            Log.LogWarning("Task execution was canceled.");
            return false;
        }
    }

    private static bool IsCanceled(AggregateException ex)
    {
        var flattened = ex.Flatten();
        for (var i = 0; i < flattened.InnerExceptions.Count; i++)
        {
            if (flattened.InnerExceptions[i] is OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Executes the task compilation logic.</summary>
    protected virtual bool ExecuteCore()
    {
        CancellationToken.ThrowIfCancellationRequested();

        var sources = GetSources();
        if (sources.Length == 0)
        {
            return true;
        }

        if (!Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }

        var outputItems = new ConcurrentBag<ITaskItem>();

        if (!OnBeforeExecute())
        {
            return false;
        }

        var hasErrors = false;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
            CancellationToken = CancellationToken
        };
        Parallel.ForEach(sources, options, sourceItem =>
        {
            if (hasErrors)
            {
                return;
            }

            var taskItems = ProcessSourceItem(sourceItem);
            if (taskItems == null)
            {
                hasErrors = true;
                return;
            }

            foreach (var item in taskItems)
            {
                outputItems.Add(item);
            }
        });

        if (hasErrors)
        {
            return false;
        }

        SetOutputItems([.. outputItems]);
        return true;
    }

    protected virtual bool OnBeforeExecute() => true;

    public bool DisableVcvarsCache { get; set; } = false;

    protected Dictionary<string, string>? GetVcvarsEnvironment(string vcvarsArch)
    {
        if (Toolchain is MsvcToolchain msvc)
        {
            return msvc.GetVcvarsEnvironment(vcvarsArch, CancellationToken);
        }

        return Toolchain.GetEnvironmentVariables(vcvarsArch);
    }

    protected void SafeDeleteFiles(params string[] paths) => CleanupFiles(paths);

    internal static void CleanupFiles(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore cleanup errors on best-effort basis
            }
        }
    }
}
