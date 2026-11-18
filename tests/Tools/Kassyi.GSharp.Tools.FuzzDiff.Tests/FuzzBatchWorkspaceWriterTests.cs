#nullable enable

using System.Linq;
using System.Xml.Linq;

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Tests that the batch projects <c>fuzz-diff run</c> builds compile the same differential harness as the test
/// assembly of their dialect, so that the partial members of the shared harness have their bodies.
/// </summary>
public sealed class FuzzBatchWorkspaceWriterTests
{
    private const string RelRepo = "../..";

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu", "Cuda")]
    [InlineData("Cuda", "Cpu")]
    public void BuildProjectXml_DialectHarnessFiles_CompilesEveryFileOfItsOwnDialectOnly(string dialect, string other)
    {
        string xml = FuzzBatchWorkspaceWriter.BuildProjectXml(dialect, "Generated", RelRepo, cudaArch: null);
        var items = XDocument.Parse(xml)
            .Descendants("Compile")
            .Select(static e => (Include: (string?)e.Attribute("Include") ?? "", Exclude: (string?)e.Attribute("Exclude") ?? ""))
            .ToList();

        string ownFolder = HarnessFolder(dialect);
        string[] ownFiles = Directory.GetFiles(
            RepositoryPaths.Combine("tests", "Compiler", $"Kassyi.GSharp.Tests.{dialect}", "Differential", "Harness"),
            "*.cs");
        Assert.NotEmpty(ownFiles);
        foreach (string file in ownFiles)
        {
            string name = Path.GetFileName(file);
            Assert.True(
                items.Any(item =>
                    (item.Include == $"{RelRepo}/{ownFolder}/*.cs" || item.Include == $"{RelRepo}/{ownFolder}/{name}") &&
                    !item.Exclude.Contains(name, StringComparison.Ordinal) &&
                    !item.Exclude.Contains($"{ownFolder}/*", StringComparison.Ordinal)),
                $"The {dialect} batch project does not compile {ownFolder}/{name}.");
        }

        string otherFolder = HarnessFolder(other);
        Assert.DoesNotContain(items, item => item.Include.StartsWith($"{RelRepo}/{otherFolder}/", StringComparison.Ordinal));
    }

    private static string HarnessFolder(string dialect) =>
        $"tests/Compiler/Kassyi.GSharp.Tests.{dialect}/Differential/Harness";
}
