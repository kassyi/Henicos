using Xunit;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;

[SkipSnapshotConsistency("Analyzer test case")]
[Category(TestCategory.Compiler)]
public class InvalidTestCases
{
    // Forbidden Test_ prefix
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Add_TwoNegativeNumbers_ReturnsNegative()
    {
    }

    // Forbidden Test prefix (followed by uppercase)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void TestAdd_TwoNegativeNumbers_ReturnsNegative()
    {
    }

    // Single segment (starts with Test)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void TestMethod()
    {
    }

    // Single segment without underscore
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void SimpleExecution()
    {
    }

    // Double underscore
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Two__Underscores_Invalid()
    {
    }

    // Trailing underscore
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void TrailingUnderscore_()
    {
    }

    // Leading underscore
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void _LeadingUnderscore_Invalid()
    {
    }

    // Lowercase first char
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void lowercaseFirst_Condition_Expected()
    {
    }

    // Lowercase segment after underscore
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Add_lowercaseSecond_Expected()
    {
    }

    // 5 segments (exceeds {1,3} underscores)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void One_Two_Three_Four_Five()
    {
    }

    // SkippableFact with invalid name
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_SkippableFact_Invalid()
    {
    }

    // Theory with invalid name
    [Theory]
    [Scenario(TestScenario.SadPath)]
    public void Test_Theory_Invalid(int x)
    {
    }
}
