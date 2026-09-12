using Microsoft.Build.Framework;

namespace Kassyi.GSharp.Compiler.Tasks;

public abstract class CompileKernelTaskBase : Microsoft.Build.Utilities.Task
{
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    protected abstract ITaskItem[] GetSources();
    protected abstract ITaskItem[]? ProcessSourceItem(ITaskItem sourceItem);
    protected abstract void SetOutputItems(ITaskItem[] outputItems);

    public override bool Execute()
    {
        var sources = GetSources();
        if (sources.Length == 0)
        {
            return true;
        }

        if (!Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }

        var outputItems = new List<ITaskItem>();

        if (!OnBeforeExecute())
        {
            return false;
        }

        foreach (var sourceItem in sources)
        {
            var taskItems = ProcessSourceItem(sourceItem);
            if (taskItems == null)
            {
                return false;
            }

            outputItems.AddRange(taskItems);
        }

        SetOutputItems([.. outputItems]);
        return true;
    }

    protected virtual bool OnBeforeExecute()
    {
        return true;
    }

    protected void SafeDeleteFiles(params string[] paths)
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
