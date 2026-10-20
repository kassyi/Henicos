using Kassyi.GSharp.Naming;

namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>Maps the renamed identifiers in native compiler output back to their C# names.</summary>
/// <remarks>The compiler output is logged unchanged; the mapping is an extra message so that the generated source stays searchable.</remarks>
internal static class RenamedIdentifierLogger
{
    public static void LogRenamedIdentifiers(TaskLoggingHelper log, string? standardOutput, string? standardError)
    {
        var mapping = GSharpNameEncoding.DescribeRenamedIdentifiers((standardOutput ?? string.Empty) + "\n" +
                                                                    (standardError ?? string.Empty));
        if (mapping.Length > 0)
        {
            log.LogMessage(MessageImportance.High, $"G# identifiers in the compiler output: {mapping}");
        }
    }
}
