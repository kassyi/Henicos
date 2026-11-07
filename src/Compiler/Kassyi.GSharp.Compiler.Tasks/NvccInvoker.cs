using System.Text;

namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>Builds the nvcc command line, runs nvcc and reports its output to the MSBuild log.</summary>
internal static class NvccInvoker
{
    // Everything on the nvcc command line except the output and the sources.
    public static string BuildOptions(string[] architectures, bool isWindows)
    {
        var args = new StringBuilder();
        args.Append("--fatbin ");
        args.Append("-std=c++17 ");
        // -diag-suppress 177 = Synthesized switch-case labels (__gsharp_switch_<n>_state_<case>) and prelude guard locals
        // (__gsharp_sort_compare_scope) are unreferenced
        // -diag-suppress 550 = Synthesized locals or shared memory buffers (__shared__ int __gsharp_id_<n>_b[...]) are
        // assigned but never read
        args.Append("-diag-suppress 177,550 ");
        if (isWindows)
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

    public static string BuildArguments(string nvccOptions, IReadOnlyList<string> sourcePaths, string outputFatbinPath)
    {
        var args = new StringBuilder(nvccOptions);
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

    public static bool Run(
        TaskLoggingHelper log,
        IProcessRunner processRunner,
        string nvccPath,
        Dictionary<string, string>? vcvarsEnv,
        IReadOnlyList<string> sourcePaths,
        string arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = processRunner.Run(CreateStartInfo(nvccPath, vcvarsEnv, arguments), cancellationToken);
            var reportPath = sourcePaths.Count > 0 ? sourcePaths[0] : "";
            var suppressedFunctions = CollectSuppressedSpillFunctions(log, sourcePaths);

            if (NvccOutputParser.DetectRegisterSpill(
                    result.StandardOutput, result.StandardError, out var errorMessage, suppressedFunctions))
            {
                log.LogWarning(subcategory: null, warningCode: "GS0001", helpKeyword: null, file: reportPath,
                    lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0, message: errorMessage);
            }

            if (result.ExitCode == 0)
            {
                LogSuccess(log, reportPath, result, suppressedFunctions);
                return true;
            }

            LogFailure(log, result);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError($"Failed to start nvcc at '{nvccPath}': {ex.Message}");
            return false;
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string nvccPath,
        Dictionary<string, string>? vcvarsEnv,
        string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = nvccPath,
            Arguments = arguments
        };

        if (vcvarsEnv != null)
        {
            foreach (var kvp in vcvarsEnv)
            {
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        return startInfo;
    }

    private static void LogSuccess(
        TaskLoggingHelper log,
        string reportPath,
        ProcessResult result,
        ISet<string> suppressedFunctions)
    {
        NvccOutputParser.LogCompilerWarnings(
            log, reportPath, result.StandardOutput, result.StandardError, suppressedFunctions);

        if (!string.IsNullOrWhiteSpace(result.StandardError) &&
            result.StandardError.IndexOf("ptxas info", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            log.LogMessage(MessageImportance.Low, result.StandardError);
        }
    }

    private static void LogFailure(TaskLoggingHelper log, ProcessResult result)
    {
        log.LogError($"nvcc compilation failed with exit code {result.ExitCode}.");
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            log.LogMessage(MessageImportance.High, result.StandardOutput);
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            log.LogError(result.StandardError);
        }

        RenamedIdentifierLogger.LogRenamedIdentifiers(log, result.StandardOutput, result.StandardError);
    }

    private static HashSet<string> CollectSuppressedSpillFunctions(
        TaskLoggingHelper log,
        IReadOnlyList<string> sourcePaths)
    {
        var suppressed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sourcePath in sourcePaths)
        {
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(sourcePath);
                foreach (var fn in SpillSuppressionParser.GetSuppressedSpillFunctions(content))
                {
                    suppressed.Add(fn);
                }
            }
            // smell-scan: ignore swallowed-exception -- the failure is written to the MSBuild log; an unreadable source
            // only means its suppress comments are not seen
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.LogMessage(MessageImportance.Low,
                    $"Could not read '{sourcePath}' to look for '// gsharp:suppress GS0001': {ex.Message}");
            }
        }

        return suppressed;
    }
}
