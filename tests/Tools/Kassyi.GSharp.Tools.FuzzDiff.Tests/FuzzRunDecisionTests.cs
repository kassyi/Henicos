#nullable enable
using System.Text.Json.Nodes;

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Tests for the decisions of <c>fuzz-diff run</c> that change what is recorded: which known issue a failure is
/// attributed to, whether the run already holds the GPU lock, and whether a locked CUDA run only waited for the lock.
/// </summary>
public class FuzzRunDecisionTests
{
    private static readonly KnownIssueEntry[] s_catalog =
    [
        Entry(1001, classifyOnlyPaths: null),
        Entry(1002, classifyOnlyPaths: ["Native"]),
        Entry(1003, classifyOnlyPaths: ["Native", "Cuda"]),
    ];

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("native", 1002)]
    [InlineData("CUDA,Native", 1003)]
    public void Classify_ExactPathSet_NamesTheFirstMatchingIssue(string paths, int expectedIssue)
    {
        var details = Failure("failed", paths);

        FuzzFindingRecorder.Classify(details, s_catalog);

        // mutant: the comparison is case-sensitive or depends on the order of the paths
        Assert.Equal(expectedIssue, details["suspectedIssue"]?.GetValue<int>());
        Assert.StartsWith($"#{expectedIssue} suspected (", details["suspectedIssueNote"]?.GetValue<string>());
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Managed,Native")]
    [InlineData("Cuda")]
    [InlineData("Managed,Native,Cuda")]
    public void Classify_SupersetOrSubsetOfThePaths_IsNotClassified(string paths)
    {
        var details = Failure("failed", paths);

        FuzzFindingRecorder.Classify(details, s_catalog);

        // mutant: a subset or superset of the classify-only paths counts as a match
        Assert.False(details.ContainsKey("suspectedIssue"));
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("compile_error")]
    [InlineData("hang")]
    public void Classify_CompileErrorOrHang_IsNeverClassified(string status)
    {
        var details = Failure(status, ["Native"]);

        FuzzFindingRecorder.Classify(details, s_catalog);

        // mutant: the status check is removed, so a build failure on Native is attributed to the issue
        Assert.False(details.ContainsKey("suspectedIssue"));
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("{\"pid\":4242}", 4242, null, true)]
    [InlineData("{\"pid\":77}", 4242, "12,77", true)]
    [InlineData("{\"pid\":78}", 4242, "12,77", false)]
    [InlineData("{\"pid\":77}", 4242, null, false)]
    public void IsAlreadyInsideGpuLock_OwnerPid_MatchesThisProcessOrALauncher(
        string ownerJson,
        int processId,
        string? launchers,
        bool expected)
    {
        string ownerPath = WriteOwner(ownerJson);

        bool inside = FuzzRunProcess.IsAlreadyInsideGpuLock(ownerPath, processId, launchers);

        // mutant: the launcher list is ignored, or any listed pid is accepted
        Assert.Equal(expected, inside);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("not json")]
    [InlineData("{\"host\":\"x\"}")]
    [InlineData("[4242]")]
    public void IsAlreadyInsideGpuLock_UnreadableOwnerFile_IsNotOurLock(string ownerJson)
    {
        string ownerPath = WriteOwner(ownerJson);

        bool inside = FuzzRunProcess.IsAlreadyInsideGpuLock(ownerPath, 4242, "4242");

        // mutant: an owner file that cannot be read throws, or is taken as our lock
        Assert.False(inside);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void IsAlreadyInsideGpuLock_NoOwnerFile_IsNotOurLock()
    {
        string ownerPath = Path.Combine(Path.GetTempPath(), $"fuzz-lock-{Guid.NewGuid():N}", "owner.json");

        bool inside = FuzzRunProcess.IsAlreadyInsideGpuLock(ownerPath, 4242, "4242");

        // mutant: a missing owner file is taken as our lock
        Assert.False(inside);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(3, false, "[gpu-lock] gave up after 10 minutes; ... is held by:", true)]
    [InlineData(3, false, "batch failed", false)]
    [InlineData(124, true, "[gpu-lock] gave up after 10 minutes", false)]
    [InlineData(1, false, "[gpu-lock] gave up after 10 minutes", false)]
    public void IsGpuLockWaitTimeout_OnlyTheLockScriptGivingUp_SkipsTheStage(
        int status,
        bool timedOut,
        string stderr,
        bool expected)
    {
        var result = new FuzzProcessResult(status, timedOut, string.Empty, stderr);

        // mutant: exit code 3 alone is read as a lock timeout, so a batch that exits with 3 is never recorded
        Assert.Equal(expected, FuzzRunProcess.IsGpuLockWaitTimeout(result));
    }

    private static KnownIssueEntry Entry(int issue, string[]? classifyOnlyPaths) =>
        new(issue, "wrong-result", $"issue {issue}", ["C", "G"], ["Native"], string.Empty, [], null, classifyOnlyPaths);

    private static JsonObject Failure(string status, string paths)
    {
        var array = new JsonArray();
        var span = paths.AsSpan();
        foreach (var range in span.Split(','))
        {
            array.Add(span[range].ToString());
        }

        return new JsonObject { ["status"] = status, ["kernel"] = "K1", ["failingPaths"] = array };
    }

    private static JsonObject Failure(string status, string[] paths)
    {
        var array = new JsonArray();
        foreach (string path in paths)
        {
            array.Add(path);
        }

        return new JsonObject { ["status"] = status, ["kernel"] = "K1", ["failingPaths"] = array };
    }

    private static string WriteOwner(string json)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"fuzz-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "owner.json");
        File.WriteAllText(path, json);
        return path;
    }
}
