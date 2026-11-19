namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>
/// Shared file I/O helpers for native build command records (deleting stale records before compilation, verifying
/// cached records, and writing records after a successful build).
/// </summary>
internal static class CommandRecordFile
{
    public static bool TryDelete(TaskLoggingHelper log, string recordFilePath, string recordDescription)
    {
        try
        {
            if (File.Exists(recordFilePath))
            {
                File.Delete(recordFilePath);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogError($"Failed to delete the {recordDescription} record '{recordFilePath}': {ex.Message}");
            return false;
        }
    }

    public static bool IsCurrent(
        TaskLoggingHelper log,
        string recordFilePath,
        string expectedRecord,
        string recordDescription)
    {
        if (!File.Exists(recordFilePath))
        {
            log.LogMessage(MessageImportance.Low, $"Recompiling because '{recordFilePath}' does not exist.");
            return false;
        }

        try
        {
            if (string.Equals(File.ReadAllText(recordFilePath), expectedRecord, StringComparison.Ordinal))
            {
                return true;
            }

            log.LogMessage(
                MessageImportance.Normal,
                $"Recompiling because the {recordDescription} differs from the one recorded in '{recordFilePath}'.");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogMessage(
                MessageImportance.Low,
                $"Recompiling because '{recordFilePath}' could not be read: {ex.Message}");
            return false;
        }
    }

    public static bool TryWrite(
        TaskLoggingHelper log,
        string recordFilePath,
        string recordContent,
        string recordDescription)
    {
        try
        {
            File.WriteAllText(recordFilePath, recordContent);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogError($"Failed to write the {recordDescription} record '{recordFilePath}': {ex.Message}");
            return false;
        }
    }
}
