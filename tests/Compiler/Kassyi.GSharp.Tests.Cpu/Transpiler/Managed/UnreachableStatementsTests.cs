#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Managed;

/// <summary>
/// Unit tests for <see cref="UnreachableStatements.FindKept"/> (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class UnreachableStatementsTests
{
    private static readonly StatementReach J = new(EndsUnconditionally: true, StopsRemoval: false);
    private static readonly StatementReach F = new(EndsUnconditionally: false, StopsRemoval: false);
    private static readonly StatementReach L = new(EndsUnconditionally: false, StopsRemoval: true);
    private static readonly StatementReach LJ = new(EndsUnconditionally: true, StopsRemoval: true);
    private static readonly StatementReach D = new(EndsUnconditionally: false, StopsRemoval: true);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void FindKept_JumpThenStatements_KeepsTheJumpOnly()
    {
        var statements = ImmutableArray.Create(J, F, F);
        var kept = UnreachableStatements.FindKept(statements);
        Assert.Equal(new[] { 0 }, kept);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void FindKept_JumpThenLabel_KeepsFromTheLabel()
    {
        var statements = ImmutableArray.Create(J, F, L, F);
        var kept = UnreachableStatements.FindKept(statements);
        Assert.Equal(new[] { 0, 2, 3 }, kept);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindKept_JumpRightBeforeADeclaration_KeepsAll()
    {
        var statements = ImmutableArray.Create(J, D, F);
        var kept = UnreachableStatements.FindKept(statements);
        Assert.Equal(new[] { 0, 1, 2 }, kept);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void FindKept_TwoJumps_RemovesAfterTheFirst()
    {
        var statements = ImmutableArray.Create(F, J, F, J, F);
        var kept = UnreachableStatements.FindKept(statements);
        Assert.Equal(new[] { 0, 1 }, kept);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void FindKept_LabelThatEnds_RemovesAfterTheLabel()
    {
        var statements = ImmutableArray.Create(J, LJ, F, F);
        var kept = UnreachableStatements.FindKept(statements);
        Assert.Equal(new[] { 0, 1 }, kept);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindKept_SingleJump_KeepsIt()
    {
        var statements = ImmutableArray.Create(J);
        var kept = UnreachableStatements.FindKept(statements);
        Assert.Equal(new[] { 0 }, kept);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindKept_Empty_IsEmpty()
    {
        var statements = ImmutableArray<StatementReach>.Empty;
        var kept = UnreachableStatements.FindKept(statements);
        Assert.Empty(kept);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindKept_NoJump_KeepsAll()
    {
        var statements = ImmutableArray.Create(F, F, D, F);
        var kept = UnreachableStatements.FindKept(statements);
        Assert.Equal(new[] { 0, 1, 2, 3 }, kept);
    }
}
