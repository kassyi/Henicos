using Kassyi.GSharp.Compiler.Helpers;
using System;
using VerifyTests;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

public static class SnapshotNamingHelper
{
    /// <summary>
    /// Configures Verify to use a unified, short hash-based filename to prevent MAX_PATH issues.
    /// Format: Snap.{Prefix}_{Hash10}[_{Dialect}] (Verify will append .g.verified.{extension})
    /// </summary>
    public static SettingsTask UseFastHashSnapshotName(this SettingsTask task, string testMethodName,
        string dialect = null)
    {
        ArgumentNullException.ThrowIfNull(testMethodName);

        var prefix = GetMeaningfulPrefix(testMethodName);
        var hash10 = HashHelper.ComputeFnv1A64ShortHash(testMethodName);
        var dialectSuffix = string.IsNullOrEmpty(dialect) ? "" : dialect;

        var snapshotName = $"Snap.{prefix}_{hash10}_{dialectSuffix}";

        return task.UseFileName(snapshotName);
    }

    /// <summary>
    /// Extracts a meaningful prefix from a test method name by splitting by '_' and picking the longest segment,
    /// which naturally avoids generic short prefixes like 'Test', 'Lower', etc.
    /// This implementation uses spans to avoid array allocations.
    /// </summary>
    public static ReadOnlySpan<char> GetMeaningfulPrefix(ReadOnlySpan<char> testMethodName)
    {
        int maxLen = 0;
        int maxStart = 0;
        int currentStart = 0;

        for (int i = 0; i <= testMethodName.Length; i++)
        {
            if (i == testMethodName.Length || testMethodName[i] == '_')
            {
                int len = i - currentStart;
                var segment = testMethodName.Slice(currentStart, len);

                // Ignore generic prefixes
                bool isGeneric = segment.Equals("Test".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                                 segment.Equals("Fact".AsSpan(), StringComparison.OrdinalIgnoreCase);

                if (!isGeneric && len > maxLen)
                {
                    maxLen = len;
                    maxStart = currentStart;
                }

                currentStart = i + 1;
            }
        }

        // Fallback if everything was generic
        if (maxLen == 0 && testMethodName.Length > 0)
        {
            maxLen = testMethodName.Length;
            maxStart = 0;
        }

        var prefix = testMethodName.Slice(maxStart, maxLen);
        if (prefix.Length > 25)
        {
            prefix = prefix.Slice(0, 25);
        }

        return prefix;
    }
}
