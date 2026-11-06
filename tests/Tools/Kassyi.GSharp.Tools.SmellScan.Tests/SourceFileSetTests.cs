#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class SourceFileSetTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void FromJsonList_Paths_AreNormalizedSortedAndDeduplicated()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("a.cs", "class A {}");
        workspace.Write("b.cs", "class B {}");
        workspace.Write("dir/c.cs", "class C {}");

        var selection = SourceFileSet.FromJsonList(
            workspace.Root,
            """["b.cs", "./a.cs", "a.cs", "dir\\c.cs", "notes.md"]""");

        Assert.Equal(["a.cs", "b.cs", "dir/c.cs"], TestValues.Strings(selection.Files));
        Assert.Empty(TestValues.Strings(selection.Missing));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void FromJsonList_FileThatDoesNotExist_IsListedAsMissing()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("a.cs", "class A {}");

        var selection = SourceFileSet.FromJsonList(workspace.Root, """["a.cs", "gone.cs"]""");

        Assert.Equal(["a.cs"], TestValues.Strings(selection.Files));
        Assert.Equal(["gone.cs"], TestValues.Strings(selection.Missing));
    }

    [Theory]
    [InlineData("""{"files": []}""")]
    [InlineData("""["a.cs", 3]""")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void FromJsonList_NotAnArrayOfStrings_Throws(string json)
    {
        using var workspace = new ScanWorkspace();

        Assert.Throws<InvalidDataException>(() => SourceFileSet.FromJsonList(workspace.Root, json));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void FromPaths_FileOutsideTheCurrentDirectory_IsMadeRelativeToTheRoot()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("dir/c.cs", "class C {}");
        workspace.Write("dir/readme.md", "text");

        var selection = SourceFileSet.FromPaths(
            workspace.Root,
            [Path.Combine(workspace.Root, "dir", "c.cs"), Path.Combine(workspace.Root, "dir", "readme.md")]);

        Assert.Equal(["dir/c.cs"], TestValues.Strings(selection.Files));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void FromPaths_PathThatDoesNotExist_IsListedAsMissing()
    {
        using var workspace = new ScanWorkspace();

        var selection = SourceFileSet.FromPaths(workspace.Root, [Path.Combine(workspace.Root, "gone.cs")]);

        Assert.Empty(TestValues.Strings(selection.Files));
        Assert.Equal(["gone.cs"], TestValues.Strings(selection.Missing));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void AllTracked_GitCheckout_ListsTrackedCSharpFilesThatStillExist()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("a.cs", "class A {}");
        workspace.Write("dir/b.cs", "class B {}");
        workspace.Write("dir/deleted.cs", "class D {}");
        workspace.Write("notes.md", "text");
        workspace.Write("untracked.cs", "class U {}");
        Assert.NotNull(GitRunner.TryRun(workspace.Root, "init", "--quiet"));
        Assert.NotNull(GitRunner.TryRun(workspace.Root, "add", "a.cs", "dir/b.cs", "dir/deleted.cs", "notes.md"));
        File.Delete(Path.Combine(workspace.Root, "dir", "deleted.cs"));

        var selection = SourceFileSet.AllTracked(workspace.Root);

        Assert.Equal(["a.cs", "dir/b.cs"], TestValues.Strings(selection.Files));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void FromPaths_Directory_ListsTheTrackedFilesBelowIt()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("a.cs", "class A {}");
        workspace.Write("dir/b.cs", "class B {}");
        Assert.NotNull(GitRunner.TryRun(workspace.Root, "init", "--quiet"));
        Assert.NotNull(GitRunner.TryRun(workspace.Root, "add", "a.cs", "dir/b.cs"));

        var selection = SourceFileSet.FromPaths(workspace.Root, [Path.Combine(workspace.Root, "dir")]);

        Assert.Equal(["dir/b.cs"], TestValues.Strings(selection.Files));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void AllTracked_DirectoryThatIsNotACheckout_Throws()
    {
        using var workspace = new ScanWorkspace();

        Assert.Throws<InvalidOperationException>(() => SourceFileSet.AllTracked(workspace.Root));
    }
}
