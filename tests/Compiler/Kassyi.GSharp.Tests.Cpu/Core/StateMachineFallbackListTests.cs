#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Cpu.Core;

[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
public class StateMachineFallbackListTests
{
    internal static bool IsManagedStackBuild() =>
        typeof(StateMachineFallbackListTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(static a => string.Equals(a.Key, "GSharpManagedBarrierModel", StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(a.Value, "Stack", StringComparison.OrdinalIgnoreCase));

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void StateMachineFallbacks_StackModelBuild_MatchesExpectedList()
    {
        var isNative = NativeStackTests.IsStackBuild();
        var isManaged = IsManagedStackBuild();
        Skip.IfNot(isNative || isManaged, "Runs only when GSharpNativeBarrierModel=Stack or GSharpManagedBarrierModel=Stack.");

#if GSHARP_NATIVE_STACK || GSHARP_MANAGED_STACK
        var generatedSource = Kassyi.GSharp.Generated.GSharpStateMachineFallbacks.Source;
#else
        var generatedSource = string.Empty;
#endif
        var actualRows = ParseActualRows(generatedSource, isNative, isManaged);
        AssertSorted(actualRows);

        var expectedPath = RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", "Core", "StateMachineFallbacks.expected.txt");
        Assert.True(File.Exists(expectedPath), $"Missing expected fallback list: {expectedPath}");

        var expectedRows = ParseExpectedRows(File.ReadAllText(expectedPath), isNative, isManaged);
        AssertMatches(expectedRows, actualRows);
    }

    private static List<(string Kind, string Method, string Reason)> ParseActualRows(
        string source, bool isNative, bool isManaged)
    {
        var rows = new List<(string Kind, string Method, string Reason)>();
        foreach (var rawLine in source.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            if (line.IsEmpty)
            {
                continue;
            }

            var (kind, method, reason, issue) = SplitTabs(line, expectedColumns: 3);
            Assert.False(method.Length == 0 || reason.Length == 0, $"Empty method or reason in fallback row: '{line}'");
            _ = issue;

            if (string.Equals(kind, "native", StringComparison.Ordinal))
            {
                Assert.True(isNative, $"Unexpected native fallback in non-native-stack build: '{line}'");
            }
            else if (string.Equals(kind, "managed", StringComparison.Ordinal))
            {
                Assert.True(isManaged, $"Unexpected managed fallback in non-managed-stack build: '{line}'");
            }
            else
            {
                Assert.Fail($"Unknown fallback kind '{kind}' in row: '{line}'");
            }

            rows.Add((kind, method, reason));
        }

        return rows;
    }

    private static List<(string Kind, string Method, string Reason)> ParseExpectedRows(
        string text, bool isNative, bool isManaged)
    {
        var rows = new List<(string Kind, string Method, string Reason)>();
        foreach (var rawLine in text.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            if (line.IsEmpty || line[0] == '#')
            {
                continue;
            }

            var (kind, method, reason, issue) = SplitTabs(line, expectedColumns: 4);
            Assert.False(method.Length == 0 || reason.Length == 0, $"Empty method or reason in expected row: '{line}'");
            Assert.True(IsValidIssueTag(issue.AsSpan()), $"Expected #<issue> tag in expected row '{line}', found '{issue}'.");

            if (string.Equals(kind, "native", StringComparison.Ordinal))
            {
                if (isNative)
                {
                    rows.Add((kind, method, reason));
                }
            }
            else if (string.Equals(kind, "managed", StringComparison.Ordinal))
            {
                if (isManaged)
                {
                    rows.Add((kind, method, reason));
                }
            }
            else
            {
                Assert.Fail($"Unknown expected kind '{kind}' in row: '{line}'");
            }
        }

        return rows;
    }

    private static (string Col0, string Col1, string Col2, string Col3) SplitTabs(
        ReadOnlySpan<char> line, int expectedColumns)
    {
        var count = 0;
        var col0 = string.Empty;
        var col1 = string.Empty;
        var col2 = string.Empty;
        var col3 = string.Empty;
        foreach (var part in line.Split('\t'))
        {
            switch (count)
            {
                case 0:
                    col0 = line[part].ToString();
                    break;
                case 1:
                    col1 = line[part].ToString();
                    break;
                case 2:
                    col2 = line[part].ToString();
                    break;
                case 3:
                    col3 = line[part].ToString();
                    break;
            }

            count++;
        }

        Assert.Equal(expectedColumns, count);
        return (col0, col1, col2, col3);
    }

    private static bool IsValidIssueTag(ReadOnlySpan<char> tag)
    {
        if (tag.Length < 2 || tag[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < tag.Length; i++)
        {
            if (!char.IsAsciiDigit(tag[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void AssertSorted(List<(string Kind, string Method, string Reason)> actualRows)
    {
        for (var i = 1; i < actualRows.Count; i++)
        {
            Assert.True(
                CompareRows(actualRows[i - 1], actualRows[i]) <= 0,
                $"Fallback rows are not sorted at index {i}: '{FormatRow(actualRows[i - 1])}' before '{FormatRow(actualRows[i])}'.");
        }
    }

    private static int CompareRows(
        (string Kind, string Method, string Reason) left,
        (string Kind, string Method, string Reason) right)
    {
        var kindCmp = StringComparer.Ordinal.Compare(left.Kind, right.Kind);
        if (kindCmp != 0)
        {
            return kindCmp;
        }

        var methodCmp = StringComparer.Ordinal.Compare(left.Method, right.Method);
        return methodCmp != 0 ? methodCmp : StringComparer.Ordinal.Compare(left.Reason, right.Reason);
    }

    private static void AssertMatches(
        List<(string Kind, string Method, string Reason)> expectedRows,
        List<(string Kind, string Method, string Reason)> actualRows)
    {
        expectedRows.Sort(CompareRows);
        var expectedCounts = CountRows(expectedRows);
        var actualCounts = CountRows(actualRows);

        var unexpected = new List<string>();
        foreach (var (line, count) in actualCounts)
        {
            expectedCounts.TryGetValue(line, out var expCount);
            for (var i = expCount; i < count; i++)
            {
                unexpected.Add(line);
            }
        }

        var stale = new List<string>();
        foreach (var (line, count) in expectedCounts)
        {
            actualCounts.TryGetValue(line, out var actCount);
            for (var i = actCount; i < count; i++)
            {
                stale.Add(line);
            }
        }

        if (unexpected.Count == 0 && stale.Count == 0)
        {
            return;
        }

        var lines = new List<string>();
        if (unexpected.Count > 0)
        {
            lines.Add($"Unexpected state-machine fallbacks ({unexpected.Count}):");
            foreach (var item in unexpected)
            {
                lines.Add($"  + {item}");
            }
        }

        if (stale.Count > 0)
        {
            lines.Add($"Stale entries in StateMachineFallbacks.expected.txt ({stale.Count}):");
            foreach (var item in stale)
            {
                lines.Add($"  - {item}");
            }
        }

        Assert.Fail(string.Join(Environment.NewLine, lines));
    }

    private static Dictionary<string, int> CountRows(List<(string Kind, string Method, string Reason)> rows)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var line = FormatRow(row);
            counts[line] = counts.TryGetValue(line, out var existing) ? existing + 1 : 1;
        }

        return counts;
    }

    private static string FormatRow((string Kind, string Method, string Reason) row) =>
        $"{row.Kind}\t{row.Method}\t{row.Reason}";
}
