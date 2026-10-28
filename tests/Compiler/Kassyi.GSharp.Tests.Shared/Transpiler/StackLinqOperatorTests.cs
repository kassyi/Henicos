using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// <see cref="StackLinqOperators"/> is the one list of the StackLinq operator names that a fused device chain can contain;
/// the chain extractor, the fusion rule and the parallel aggregate planner read the operator from it.
/// </summary>
public class StackLinqOperatorTests
{
    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Where", false, false)]
    [InlineData("Select", false, false)]
    [InlineData("Take", false, true)]
    [InlineData("Skip", false, true)]
    [InlineData("TakeWhile", false, false)]
    [InlineData("SkipWhile", false, false)]
    [InlineData("TakeLast", false, true)]
    [InlineData("SkipLast", false, true)]
    [InlineData("Sum", true, false)]
    [InlineData("Count", true, false)]
    [InlineData("Any", true, false)]
    [InlineData("All", true, false)]
    [InlineData("First", true, false)]
    [InlineData("FirstOrDefault", true, false)]
    [InlineData("Last", true, false)]
    [InlineData("LastOrDefault", true, false)]
    [InlineData("Max", true, false)]
    [InlineData("Min", true, false)]
    [InlineData("Average", true, false)]
    [InlineData("Aggregate", true, false)]
    [InlineData("Contains", true, false)]
    public void Test_TryParse_FusedOperator_ReadsItsKind(string name, bool isTerminal, bool isPositionStage)
    {
        // mutant: StackLinqOperators.TryParse maps a name to the operator next to it (nameof(Max) => Min)
        Assert.True(StackLinqOperators.TryParse(name, out var op));
        Assert.Equal(name, op.ToString());
        // mutant: a stage placed after Sum in StackLinqOperator (IsTerminal compares the order)
        Assert.Equal(isTerminal, op.IsTerminal());
        Assert.Equal(isPositionStage, op.IsPositionStage());
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("AsParallel")]
    [InlineData("AsStackEnumerable")]
    [InlineData("SelectMany")]
    [InlineData("where")]
    [InlineData("0")]
    [InlineData("")]
    public void Test_TryParse_OtherName_ReturnsFalse(string name)
    {
        Assert.False(StackLinqOperators.TryParse(name, out _));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryParse_EveryOperator_RoundTrips()
    {
        // A new operator without a TryParse arm cannot be read from a method name
        foreach (var op in Enum.GetValues<StackLinqOperator>())
        {
            Assert.True(StackLinqOperators.TryParse(op.ToString(), out var parsed), op.ToString());
            Assert.Equal(op, parsed);
        }
    }
}
