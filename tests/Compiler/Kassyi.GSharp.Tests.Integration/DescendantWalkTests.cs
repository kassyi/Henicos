#nullable enable
using System.Diagnostics;
using Kassyi.GSharp.Compiler.Tasks.Helpers;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Integration;

[Category(TestCategory.Compiler)]
public class DescendantWalkTests
{
    private const int Root = 100;
    private const long RootStart = 1_000;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Visit_ChildAndGrandchild_ActsTopDown()
    {
        var table = new[] { Entry(300, 200, 1_200), Entry(200, Root, 1_100), Entry(400, 999, 1_300) };
        var acted = new List<int>();

        var handled = DescendantWalk.Visit(Root, RootStart, () => table, Recorder(acted));

        Assert.Equal([200, 300], acted);
        Assert.Equal(new Dictionary<int, long> { [200] = 1_100, [300] = 1_200 }, handled);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Visit_ChildOlderThanItsParent_LeavesItAndItsSubtreeAlone()
    {
        // 200 names the root as its parent but was created before it: an earlier owner of the reused ID created it.
        var table = new[] { Entry(200, Root, 900), Entry(300, 200, 950) };
        var acted = new List<int>();

        var handled = DescendantWalk.Visit(Root, RootStart, () => table, Recorder(acted));

        Assert.Empty(acted);
        Assert.Empty(handled);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Visit_ParentGoneFromTheNextTable_StillReachesItsChildren()
    {
        // The first table shows only the child; the next one shows a grandchild started meanwhile, while the child,
        // killed by the first pass, is gone from it.
        var tables = new Queue<ProcessEntry[]>([
            [Entry(200, Root, 1_100)],
            [Entry(300, 200, 1_200)],
            [Entry(300, 200, 1_200)],
        ]);
        var acted = new List<int>();

        DescendantWalk.Visit(
            Root,
            RootStart,
            () => tables.Count > 1 ? tables.Dequeue() : tables.Peek(),
            Recorder(acted)
        );

        Assert.Equal([200, 300], acted);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Visit_ActionFindsTheProcessGone_DoesNotWalkBelowIt()
    {
        var table = new[] { Entry(200, Root, 1_100), Entry(300, 200, 1_200) };
        var asked = new List<int>();

        var handled = DescendantWalk.Visit(Root, RootStart, () => table, (entry, _) =>
        {
            asked.Add(entry.ProcessId);
            return null;
        });

        Assert.Empty(handled);
        Assert.Equal([200], asked);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Visit_NewProcessEveryTable_StopsAfterMaxRounds()
    {
        var next = 200;
        var reads = 0;

        DescendantWalk.Visit(
            Root,
            RootStart,
            () =>
            {
                reads++;
                return [Entry(next++, Root, 1_100)];
            },
            static (entry, _) => entry.StartTime
        );

        Assert.Equal(DescendantWalk.MaxRounds, reads);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("4242 (cc1plus) S 4200 4242 4200 0 -1 4194304 1 0 0 0 0 0 0 0 20 0 1 0 987654 0 0", 4200, 987654)]
    [InlineData("4242 (a) (b c)) R 17 4242 17 0 -1 0 0 0 0 0 0 0 0 0 20 0 1 0 55 0 0", 17, 55)]
    public void TryParseStat_CommandNameWithSpacesAndParentheses_ReadsTheFieldsAfterIt(
        string stat,
        int expectedParent,
        long expectedStart
    )
    {
        Assert.True(LinuxProcessTable.TryParseStat(stat, out var parent, out var start));
        Assert.Equal(expectedParent, parent);
        Assert.Equal(expectedStart, start);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void TryParseStat_TruncatedLine_ReturnsFalse()
    {
        Assert.False(LinuxProcessTable.TryParseStat("4242 (sh) S 1 2 3", out _, out _));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Kill_ProcessOutsideAnyJobOrGroup_KillsTheGrandchildByTheWalk()
    {
        // The walk alone, as for a child that started its compiler before it joined the job, or on macOS.
        using var probe = DescendantProcessProbe.Create();
        var startInfo = probe.WaitingShell(60);
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        using var shell = Process.Start(startInfo) ?? throw new InvalidOperationException("The shell did not start.");
        Assert.True(probe.WaitFor(static n => n == 1, TimeSpan.FromSeconds(30)), "The grandchild did not start.");

        if (OperatingSystem.IsWindows())
        {
            ProcessTree.KillProcessOnly(shell);
            WindowsDescendants.Kill(shell, shell.Id, job: null);
        }
        else
        {
            UnixDescendants.Kill(shell, shell.Id);
        }

        Assert.True(probe.WaitFor(static n => n == 0, TimeSpan.FromSeconds(10)), "The grandchild survived the walk.");
        var output = shell.StandardOutput.ReadToEndAsync();
        Assert.True(output.Wait(TimeSpan.FromSeconds(10)), "A process still holds the output pipe.");
    }

    private static ProcessEntry Entry(int id, int parent, long start) => new(id, parent, start);

    private static Func<ProcessEntry, long, long?> Recorder(List<int> acted) => (entry, parentStart) =>
    {
        acted.Add(entry.ProcessId);
        return entry.StartTime >= parentStart ? entry.StartTime : null;
    };
}
