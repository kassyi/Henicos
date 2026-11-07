using System.Text;

namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>
/// The file next to a fatbin that records the nvcc command the fatbin was built with, so that a changed command
/// rebuilds a fatbin that is newer than its sources.
/// </summary>
internal static class NvccCommandRecord
{
    // [WHY] Timestamps alone miss a change of the nvcc command: a new default or GSharpTargetArchitectures value, another
    // nvcc, a new signing key, or a source added to a combined fatbin while older than the fatbin. Like MSBuild's
    // CoreCompileInputs.cache, the record holds what decides the output except the output path:
    // <options>\n<source>\n...signing-key <public key>\n<last line of nvcc --version>
    // Not recorded (a change needs a rebuild by hand, as in a C++ build without dependency files): the
    // NVCC_PREPEND_FLAGS / NVCC_APPEND_FLAGS environment variables, the version of the host C++ compiler, and the headers
    // that the sources include.
    public static string Build(string nvccOptions, string nvccVersion, IReadOnlyList<string> sourcePaths)
    {
        var record = new StringBuilder();
        record.Append(nvccOptions).Append('\n');
        foreach (var sourcePath in sourcePaths)
        {
            record.Append(sourcePath).Append('\n');
        }

        record.Append("signing-key ").Append(ArtifactHelper.GetSigningKeyFingerprint()).Append('\n');
        record.Append(nvccVersion);
        return record.ToString();
    }

    // Removes the record before nvcc runs: when nvcc, signing or the record write fails, no record of an earlier command
    // can match the fatbin left behind, so the next build compiles again
    public static bool Delete(TaskLoggingHelper log, string commandFilePath)
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
            log.LogError($"Failed to delete the nvcc command record '{commandFilePath}': {ex.Message}");
            return false;
        }
    }

    public static bool IsCurrent(TaskLoggingHelper log, string commandFilePath, string commandRecord)
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
                $"Recompiling because the nvcc command differs from the one recorded in '{commandFilePath}'.");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogMessage(MessageImportance.Low,
                $"Recompiling because '{commandFilePath}' could not be read: {ex.Message}");
            return false;
        }
    }

    public static bool Write(TaskLoggingHelper log, string commandFilePath, string commandRecord)
    {
        try
        {
            File.WriteAllText(commandFilePath, commandRecord);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogError($"Failed to write the nvcc command record '{commandFilePath}': {ex.Message}");
            return false;
        }
    }
}
