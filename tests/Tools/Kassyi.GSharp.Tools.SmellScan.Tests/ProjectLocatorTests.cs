#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class ProjectLocatorTests
{
    [Theory]
    [InlineData("src/App/Code/File.cs", "src/App")]
    [InlineData("src/App/File.cs", "src/App")]
    [InlineData("tests/Shared/Lib/File.cs", "tests/Shared/Lib")]
    [InlineData("src/Shared/Naming/Names.cs", "src/Shared")]
    [InlineData("src/Shared/Names.cs", "src/Shared")]
    [InlineData("tools/script.cs", "tools")]
    [InlineData("Top.cs", ".")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Find_File_BelongsToTheNearestProjectOrItsFirstFolders(string path, string expected)
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("src/App/App.csproj", "<Project />");
        workspace.Write("tests/Shared/Lib/Lib.shproj", "<Project />");

        Assert.Equal(expected, new ProjectLocator(workspace.Root).Find(path));
    }
}
