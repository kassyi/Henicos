using System.Text;

namespace Kassyi.GSharp.Compiler.Tasks;

internal static class NvccOutputParser
{
    private static bool HasSpill(string line)
    {
        const string Target = "bytes spill";
        var index = 0;
        while (index < line.Length)
        {
            var found = line.IndexOf(Target, index, StringComparison.OrdinalIgnoreCase);
            if (found <= 0)
            {
                return false;
            }

            var i = found - 1;
            while (i >= 0 && char.IsWhiteSpace(line[i]))
            {
                i--;
            }

            var endOfDigits = i;
            while (i >= 0 && char.IsDigit(line[i]))
            {
                i--;
            }

            var startOfDigits = i + 1;
            if (startOfDigits <= endOfDigits && line[startOfDigits] != '0')
            {
                return true;
            }

            index = found + Target.Length;
        }

        return false;
    }

    public static bool DetectRegisterSpill(
        string output,
        string error,
        out string errorMessage,
        ISet<string>? suppressedFunctions = null)
    {
        var spillLines = new List<string>();
        CollectSpills(error, suppressedFunctions, spillLines);
        CollectSpills(output, suppressedFunctions, spillLines);

        if (spillLines.Count == 0)
        {
            errorMessage = "";
            return false;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Register spill detected during PTX compilation.");
        sb.AppendLine("Details:");
        var distinctSpills = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spill in spillLines)
        {
            if (distinctSpills.Add(spill))
            {
                sb.AppendLine($"  - {spill}");
            }
        }

        sb.Append(
            "Please reduce register pressure (e.g. by avoiding excessive inlining or large stack allocations in " +
            "GPU kernels).");

        errorMessage = sb.ToString();
        return true;
    }

    private static void CollectSpills(string text, ISet<string>? suppressedFunctions, List<string> spillLines)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var functionContext = "";
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.IndexOf("Function properties for", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                functionContext = line.AsSpan().Trim().ToString();
            }

            var isWarningSpill = line.IndexOf("ptxas warning", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                 line.IndexOf("spill", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isWarningSpill && !HasSpill(line))
            {
                continue;
            }

            var trimmed = line.AsSpan().Trim().ToString();
            if (SpillSuppressionParser.IsFunctionSuppressed(trimmed, suppressedFunctions) ||
                (!isWarningSpill && SpillSuppressionParser.IsFunctionSuppressed(functionContext, suppressedFunctions)))
            {
                continue;
            }

            if (isWarningSpill)
            {
                spillLines.Add(trimmed);
            }
            else if (!string.IsNullOrEmpty(functionContext))
            {
                spillLines.Add($"{functionContext} -> {trimmed}");
            }
            else
            {
                spillLines.Add(trimmed);
            }
        }
    }

    public static IReadOnlyList<NativeCompilerWarning> ExtractWarnings(
        string output,
        string error,
        ISet<string>? suppressedFunctions = null)
    {
        var warnings = new List<NativeCompilerWarning>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        ExtractWarningsFromStream(output, suppressedFunctions, warnings, seen);
        ExtractWarningsFromStream(error, suppressedFunctions, warnings, seen);
        return warnings;
    }

    private static void ExtractWarningsFromStream(
        string text,
        ISet<string>? suppressedFunctions,
        List<NativeCompilerWarning> warnings,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        using var reader = new StringReader(text);
        string? rawLine;
        while ((rawLine = reader.ReadLine()) != null)
        {
            var line = rawLine.AsSpan().Trim().ToString();
            if (line.Length == 0)
            {
                continue;
            }

            // Register spill lines are handled by DetectRegisterSpill (GS0001).
            if (HasSpill(line) ||
                (line.IndexOf("ptxas warning", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 line.IndexOf("spill", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                continue;
            }

            // Stack size indeterminate warnings on functions with GS0001 suppression (intentional recursion tests).
            if (line.IndexOf("ptxas warning", StringComparison.OrdinalIgnoreCase) >= 0 &&
                line.IndexOf("Stack size for entry function", StringComparison.OrdinalIgnoreCase) >= 0 &&
                SpillSuppressionParser.IsFunctionSuppressed(line, suppressedFunctions))
            {
                continue;
            }

            if (NvccWarningLineParser.TryParseWarningLine(line, out var warning) && seen.Add(line))
            {
                warnings.Add(warning);
            }
        }
    }


    public static void LogCompilerWarnings(
        TaskLoggingHelper log,
        string defaultFile,
        string output,
        string error,
        ISet<string>? suppressedFunctions = null)
    {
        var warnings = ExtractWarnings(output, error, suppressedFunctions);
        if (warnings.Count == 0)
        {
            return;
        }

        foreach (var warning in warnings)
        {
            log.LogWarning(
                subcategory: null,
                warningCode: warning.Code,
                helpKeyword: null,
                file: !string.IsNullOrEmpty(warning.File) ? warning.File : defaultFile,
                lineNumber: warning.Line,
                columnNumber: warning.Column,
                endLineNumber: 0,
                endColumnNumber: 0,
                message: warning.Message);
        }

        RenamedIdentifierLogger.LogRenamedIdentifiers(log, output, error);
    }
}
