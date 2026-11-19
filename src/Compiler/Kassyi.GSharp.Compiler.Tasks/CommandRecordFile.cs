namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>
/// Reads, writes and deletes the file next to a build output that records the command the output was built with, so
/// that a changed command rebuilds an output that is newer than its sources. <c>tool</c> names the compiler in the
/// log messages (<c>nvcc</c>, <c>C++ compiler</c>).
/// </summary>
internal static class CommandRecordFile
{
    // Removes the record before the compiler runs: when the compiler, signing or the record write fails, no record of
    // an earlier command can match the output left behind, so the next build compiles again
    public static bool TryDelete(TaskLoggingHelper log, string tool, string commandFilePath)
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
            log.LogError($"Failed to delete the {tool} command record '{commandFilePath}': {ex.Message}");
            return false;
        }
    }

    public static bool IsCurrent(TaskLoggingHelper log, string tool, string commandFilePath, string commandRecord)
    {
        if (!File.Exists(commandFilePath))
        {
            log.LogMessage(MessageImportance.Low, $"Recompiling because '{commandFilePath}' does not exist.");
            return false;
        }

        try
        {
            if (string.Equals(File.ReadAllText(commandFilePath), commandRecord, StringComparison.Ordinal))
            {
                return true;
            }

            log.LogMessage(MessageImportance.Normal,
                $"Recompiling because the {tool} command differs from the one recorded in '{commandFilePath}'.");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogMessage(MessageImportance.Low,
                $"Recompiling because '{commandFilePath}' could not be read: {ex.Message}");
            return false;
        }
    }

    public static bool TryWrite(TaskLoggingHelper log, string tool, string commandFilePath, string commandRecord)
    {
        try
        {
            File.WriteAllText(commandFilePath, commandRecord);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogError($"Failed to write the {tool} command record '{commandFilePath}': {ex.Message}");
            return false;
        }
    }
}
