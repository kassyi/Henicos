#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Lowering;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// The function that runs each warp collective on the CPU stack models (<see cref="WarpShuffleShape.StackFunction"/>)
/// is the InternalNames.yml entry of the same collective, and the prelude declares each one the native stack model
/// writes.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
public class WarpShuffleShapeTests
{
    // Every collective with the name of its entry in the StackWarp group of InternalNames.yml
    private static readonly (WarpShuffleKind Kind, string Entry)[] s_entries =
    [
        (WarpShuffleKind.Sync, "ShuffleSync"),
        (WarpShuffleKind.Up, "ShuffleUpSync"),
        (WarpShuffleKind.Down, "ShuffleDownSync"),
        (WarpShuffleKind.Xor, "ShuffleXorSync"),
        (WarpShuffleKind.Ballot, "BallotSync"),
        (WarpShuffleKind.All, "AllSync"),
        (WarpShuffleKind.Any, "AnySync"),
        (WarpShuffleKind.ReduceAdd, "ReduceAddSync"),
        (WarpShuffleKind.ReduceMin, "ReduceMinSync"),
        (WarpShuffleKind.ReduceMax, "ReduceMaxSync"),
        (WarpShuffleKind.ReduceAnd, "ReduceAndSync"),
        (WarpShuffleKind.ReduceOr, "ReduceOrSync"),
        (WarpShuffleKind.ReduceXor, "ReduceXorSync"),
        (WarpShuffleKind.MatchAny, "MatchAnySync"),
        (WarpShuffleKind.MatchAll, "MatchAllSync"),
        (WarpShuffleKind.SyncWarp, "SyncWarp"),
        (WarpShuffleKind.ActiveMask, "ActiveMask"),
    ];

    // The kind by name: the theory data of a public member cannot name the internal enum
    public static TheoryData<string, string> CollectiveEntries()
    {
        var data = new TheoryData<string, string>();
        foreach (var (kind, entry) in s_entries)
        {
            data.Add(kind.ToString(), entry);
        }

        return data;
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(CollectiveEntries))]
    public void StackFunction_EachCollective_IsItsInternalNamesEntry(string kind, string entry)
    {
        var shape = WarpShuffleShape.ForKind(Enum.Parse<WarpShuffleKind>(kind));
        var (_, value, _) = Assert.Single(GSharpInternalNames.All, e => e.Path == "StackWarp." + entry);

        Assert.Equal(value, shape.StackFunction);
    }

    // The native stack model writes every collective but Warp.ActiveMask(), whose regions it does not place yet
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(CollectiveEntries))]
    public void StackFunction_NativeStackCollective_IsDeclaredByStackThreads(string kind, string entry)
    {
        var (_, _, preludeFile) = Assert.Single(GSharpInternalNames.All, e => e.Path == "StackWarp." + entry);

        Assert.Equal(kind == nameof(WarpShuffleKind.ActiveMask) ? null : "StackThreads.hpp", preludeFile);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CollectiveEntries_EveryCollective_IsListed()
    {
        var listed = s_entries.Select(static e => e.Kind).Order();

        var collectives = Enum.GetValues<WarpShuffleKind>().Where(static k => k != WarpShuffleKind.None).Order();
        Assert.Equal(collectives, listed);
    }
}
