#nullable enable
using System.Linq;

namespace Kassyi.GSharp.Tests.Infrastructure.Tests;

/// <summary>
/// Pins the format of <c>tests/memory-budgets.yml</c> and keeps its entries in step with the test projects.
/// </summary>
public class MemoryBudgetFileTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Parse_ReadsEntriesCommentsAndBlankLines()
    {
        const string Text = "# comment\r\n\r\nA.Tests: 1200 # measured\nB.Tests:300\n";

        IReadOnlyList<KeyValuePair<string, long>> entries = MemoryBudgetFile.Parse(Text);

        Assert.Equal([new("A.Tests", 1200L), new("B.Tests", 300L)], entries);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("A.Tests 1200", "line 1")]
    [InlineData("  A.Tests: 1200", "column 0")]
    [InlineData("A.Tests: 0", "line 1")]
    [InlineData("A.Tests: -5", "line 1")]
    [InlineData("A.Tests: 1.5", "line 1")]
    [InlineData("A.Tests: 1\nA.Tests: 2", "listed twice")]
    public void Parse_RejectsMalformedLines(string text, string expected)
    {
        var ex = Assert.Throws<FormatException>(() => MemoryBudgetFile.Parse(text));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void RepositoryFile_NamesExactlyTheTestProjects()
    {
        var budgeted = MemoryBudgetFile.Parse(File.ReadAllText(MemoryBudgetFile.FullPath))
            .Select(static e => e.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        var testProjects = Directory.EnumerateFiles(RepositoryPaths.Combine("tests"), "*.csproj", SearchOption.AllDirectories)
            .Where(static p => File.ReadAllText(p).Contains("Microsoft.NET.Test.Sdk", StringComparison.Ordinal))
            .Select(static p => Path.GetFileNameWithoutExtension(p))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(testProjects);
        Assert.Equal(testProjects, budgeted);
    }
}
