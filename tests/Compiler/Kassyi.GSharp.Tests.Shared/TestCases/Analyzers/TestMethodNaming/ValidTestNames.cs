using Xunit;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;

[SkipSnapshotConsistency("Analyzer test case")]
[Category(TestCategory.Compiler)]
public class ValidTestCases
{
    // 3 segments (<Unit>_<Condition>_<Expected>)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Add_TwoNegativeOperands_ReturnsNegativeSum()
    {
    }

    // 2 segments (<Unit>_<Condition>)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Lower_WhileLoop()
    {
    }

    // 4 segments (<Unit>_<Condition>_<Expected>_<Detail>)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Kernel_Launch_Grid1D_WithStream()
    {
    }

    // Numbers in identifiers
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    public void Int128_DivRem256_Quotient384(int x)
    {
    }

    // Legitimate unit starting with "Testing"
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Testing_ValidCondition_ExpectedBehavior()
    {
    }

    // Non-test helper method (no Fact/Theory attribute) - should be ignored even with invalid name
    public void TestHelperMethod_Ignored()
    {
    }

    // Another non-test method with Test prefix - ignored
    public void TestLegacyHelper()
    {
    }
}
