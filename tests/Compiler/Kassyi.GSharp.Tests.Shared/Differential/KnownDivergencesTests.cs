#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Tests of <c>KnownDivergences.yaml</c>: the file parses, every entry names a case and columns that exist and an issue
/// that is open, and the reader rejects what the format does not allow. A stale entry (a renamed kernel, a removed
/// column, a closed issue) fails here instead of hiding in a list nobody reads.
/// </summary>
public class KnownDivergencesTests
{
    private const string DifferentialNamespace = "Kassyi.GSharp.Tests.Shared.Differential";

    // ---- The registered entries ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_KnownDivergences_EveryEntryNamesAnExistingCaseAndColumn()
    {
        var cases = CollectCases();
        var problems = new List<string>();
        foreach (var group in KnownDivergences.All.GroupBy(static e => e.Case))
        {
            if (!cases.TryGetValue(group.Key, out var definitions))
            {
                problems.Add($"case {group.Key} (#{group.First().Issue}) is not the name of a static readonly DifferentialCase field");
                continue;
            }

            var columns = ColumnsOf(definitions);
            foreach (var entry in group)
            {
                if (entry.Column != KnownDivergence.ExceptionColumn && !columns.Contains(entry.Column))
                {
                    problems.Add($"case {entry.Case} has no column \"{entry.Column}\" (#{entry.Issue}); its columns: {string.Join(", ", columns.Order(StringComparer.Ordinal))}");
                }
            }
        }

        Assert.True(problems.Count == 0, "KnownDivergences.yaml names what does not exist:\n" + string.Join("\n", problems));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_KnownDivergences_IssuesAreListedOnceAndNoCellIsCoveredTwice()
    {
        var problems = new List<string>();
        foreach (var duplicate in KnownDivergences.Issues.GroupBy(static i => i.Number).Where(static g => g.Count() > 1))
        {
            problems.Add($"#{duplicate.Key} has {duplicate.Count()} blocks (lines {string.Join(", ", duplicate.Select(static i => i.Line))})");
        }

        var entries = KnownDivergences.All;
        for (int i = 0; i < entries.Count; i++)
        {
            for (int j = i + 1; j < entries.Count; j++)
            {
                var a = entries[i];
                var b = entries[j];
                if (a.Case == b.Case && a.Column == b.Column && (a.Paths & b.Paths) != 0 && (a.Hosts & b.Hosts) != 0)
                {
                    problems.Add($"{a.Case} \"{a.Column}\" is listed under #{a.Issue} and #{b.Issue} for {DiffPaths.Format(a.Paths & b.Paths)}");
                }
            }
        }

        Assert.NotEmpty(entries);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // The issue state lives on GitHub, so this test asks it through the gh CLI and skips where gh is missing, not signed in
    // or offline (CI runs the tests without a token).
    [SkippableFact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_KnownDivergences_EveryIssueIsOpenAndHasTheRecordedTitle()
    {
        var issues = KnownDivergences.Issues;
        var query = new StringBuilder("query($owner: String!, $name: String!) { repository(owner: $owner, name: $name) {");
        foreach (var issue in issues)
        {
            query.Append($" i{issue.Number}: issue(number: {issue.Number}) {{ state title }}");
        }

        query.Append(" } }");
        var repository = QueryGitHub(query.ToString());

        var problems = new List<string>();
        foreach (var issue in issues)
        {
            if (!repository.TryGetProperty($"i{issue.Number}", out var node) || node.ValueKind != JsonValueKind.Object ||
                !node.TryGetProperty("state", out var state))
            {
                problems.Add($"#{issue.Number} (line {issue.Line}) is not an issue of this repository");
                continue;
            }

            string title = node.GetProperty("title").GetString() ?? string.Empty;
            if (state.GetString() != "OPEN")
            {
                problems.Add($"#{issue.Number} ({title}) is {state.GetString()}: remove its entries if they no longer diverge, or move them to the open issue that owns them");
            }

            if (title != issue.Title)
            {
                problems.Add($"#{issue.Number} (line {issue.Line}) is titled \"{title}\" on GitHub, not \"{issue.Title}\"");
            }
        }

        Assert.True(problems.Count == 0, "KnownDivergences.yaml is out of date with GitHub:\n" + string.Join("\n", problems));
    }

    // ---- The reader ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_KnownDivergencesParse_ExpandsColumnsAndReadsPathsAndHosts()
    {
        // mutant: KnownDivergences.cs ParsePaths `paths |= ...` -> `paths = ...` (keeps only the last path)
        // mutant: KnownDivergences.cs ParseHosts returns DiffHost.Any when hosts is given
        const string yaml = """
            - issue: 12
              title: "A title: with a colon"
              entries:
                - case: Kernels.FirstKernel
                  paths: [Managed, Cuda]
                  columns: [a, $exception]
                - case: Kernels.SecondKernel
                  paths: [Native]
                  columns: [b]
                  hosts: [Windows]
            """;

        var list = KnownDivergences.Parse(yaml);

        Assert.Equal(new[] { new KnownDivergenceIssue(12, "A title: with a colon", 1) }, list.Issues);
        Assert.Equal(
            new[]
            {
                new KnownDivergence("Kernels.FirstKernel", "a", DiffPath.Managed | DiffPath.Cuda, 12),
                new KnownDivergence("Kernels.FirstKernel", KnownDivergence.ExceptionColumn, DiffPath.Managed | DiffPath.Cuda, 12),
                new KnownDivergence("Kernels.SecondKernel", "b", DiffPath.Native, 12, DiffHost.Windows),
            },
            list.Entries
        );
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("paths: [Gpu]\n      columns: [a]", "line 4: Unknown path 'Gpu'")]
    [InlineData("paths: [Native]\n      columns: [a]\n      hosts: [Linux]", "line 4: Unknown host 'Linux'")]
    [InlineData("paths: [Native]\n      columns: []", "line 4: 'columns' must be a non-empty list")]
    [InlineData("paths: [Native]", "line 4: 'columns' must be a non-empty list")]
    [InlineData("columns: [a]", "line 4: 'paths' must be a non-empty list")]
    public void Test_KnownDivergencesParse_RejectsAnEntryOutsideTheFormat(string entryBody, string expectedMessage)
    {
        string yaml = "- issue: 12\n  title: T\n  entries:\n    - case: Kernels.FirstKernel\n      " + entryBody + "\n";

        var ex = Assert.Throws<InvalidDataException>(() => KnownDivergences.Parse(yaml));

        Assert.Contains(expectedMessage, ex.Message);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("- issue: \"#12\"\n  title: T\n  entries: []\n", "line 1: 'issue' must be a positive issue number")]
    [InlineData("- issue: 12\n  entries: []\n", "line 1: Missing or empty 'title'")]
    [InlineData("- issue: 12\n  title: T\n", "line 1: The issue block has no 'entries'")]
    [InlineData("issue: 12\n", "line 1: Expected a sequence for the document")]
    [InlineData("- issue: 12\n  title: T\n  entries: [\n", "line 3:")]
    public void Test_KnownDivergencesParse_RejectsAnIssueBlockOutsideTheFormat(string yaml, string expectedMessage)
    {
        var ex = Assert.Throws<InvalidDataException>(() => KnownDivergences.Parse(yaml));

        Assert.Contains(expectedMessage, ex.Message);
    }

    // ---- Helpers ----

    // Every DifferentialCase held by a static field of a type of the differential tests, by case name
    private static Dictionary<string, List<DifferentialCase>> CollectCases()
    {
        Type?[] types;
        try
        {
            types = typeof(KnownDivergencesTests).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // The MSBuild task tests reference MSBuild assemblies that load only through MSBuildLocator; the types of
            // the differential tests do not depend on them and are among the loaded ones
            types = ex.Types;
        }

        // Nested types are skipped by their metadata flags before Namespace is read: Namespace and IsNested load the
        // declaring type, which may be one of the MSBuild-dependent types above. The cases are fields of top-level
        // test classes.
        return types
            .OfType<Type>()
            .Where(static t => IsTopLevel(t) && t.Namespace == DifferentialNamespace && !t.ContainsGenericParameters)
            .SelectMany(static t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(CasesIn)
            .GroupBy(static c => c.Name, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.ToList(), StringComparer.Ordinal);
    }

    private static bool IsTopLevel(Type type) =>
        (type.Attributes & TypeAttributes.VisibilityMask) is TypeAttributes.Public or TypeAttributes.NotPublic;

    private static IEnumerable<DifferentialCase> CasesIn(FieldInfo field)
    {
        if (field.FieldType == typeof(DifferentialCase))
        {
            return field.GetValue(null) is DifferentialCase single ? [single] : [];
        }

        if (typeof(IEnumerable).IsAssignableFrom(field.FieldType) && field.FieldType != typeof(string) &&
            field.GetValue(null) is IEnumerable values)
        {
            return values.OfType<DifferentialCase>();
        }

        return [];
    }

    // The column names of the buffers the cases build, for every seed and launch shape they run
    private static HashSet<string> ColumnsOf(List<DifferentialCase> definitions) =>
        definitions
            .SelectMany(static d => (d.Seeds ?? [0])
                .SelectMany(seed => d.GetShapesForSeed(seed)
                    .SelectMany(shape => d.Inputs(new DiffRandom(seed), shape).All)
                )
            ).SelectMany(static b => b.Columns ?? [b.Name])
            .ToHashSet(StringComparer.Ordinal);

    // Runs a GraphQL query against the repository of the checkout and returns data.repository; skips without gh access
    private static JsonElement QueryGitHub(string query)
    {
        var psi = new ProcessStartInfo("gh")
        {
            WorkingDirectory = RepositoryPaths.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { "api", "graphql", "-F", "owner={owner}", "-F", "name={repo}", "-f", "query=" + query })
        {
            psi.ArgumentList.Add(argument);
        }

        (int ExitCode, string Stdout, string Stderr, bool TimedOut) result;
        try
        {
            result = CudaTrapSubprocessRunner.RunProcessWithTimeout(psi, timeoutMs: 60_000);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Skip.If(true, $"The gh CLI is not available: {ex.Message}");
            throw;
        }

        Skip.If(result.TimedOut, "gh api graphql did not answer within 60 seconds.");

        // An issue number that does not exist makes gh exit with 1 but still print the data of the others
        JsonElement repository = default;
        bool hasData = false;
        if (result.Stdout.TrimStart().StartsWith('{'))
        {
            using var document = JsonDocument.Parse(result.Stdout);
            if (document.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("repository", out var repositoryNode) &&
                repositoryNode.ValueKind == JsonValueKind.Object)
            {
                repository = repositoryNode.Clone();
                hasData = true;
            }
        }

        Skip.IfNot(hasData, $"gh api graphql gave no data (exit code {result.ExitCode}): {result.Stderr.Trim()}");
        return repository;
    }
}
