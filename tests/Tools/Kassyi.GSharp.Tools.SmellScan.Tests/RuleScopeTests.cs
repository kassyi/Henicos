#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class RuleScopeTests
{
    private static readonly SmellConfig s_config =
        SmellConfigReader.Read("valid.yml", TestCaseFiles.Read("Config/valid.yml"));

    private static readonly RuleScope s_scope = new(s_config);

    private static List<string> RuleIds(string path)
    {
        var ids = new List<string>();
        foreach (var rule in s_scope.Rules(path))
        {
            ids.Add(rule.Id);
        }

        return ids;
    }

    private static int MaxLines(string path) =>
        s_scope.ParametersFor(s_config.Rules[0], path).Get("max_code_lines");

    [Theory]
    [InlineData("src/a/B.cs", true)]
    [InlineData("lib/B.cs", true)]
    [InlineData("other/B.cs", false)]
    [InlineData("src/skip/B.cs", false)]
    [InlineData("src/obj/B.cs", false)]
    [InlineData("generated/B.cs", false)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Rules_Path_AppliesIncludeAfterTheExcludes(string path, bool applies)
    {
        Assert.Equal(applies, RuleIds(path).Contains("long-method"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Rules_PathThatMatchesIncludeAndRuleExclude_IsExcluded()
    {
        // src/skip/B.cs matches the include "src/**" and the exclude "src/skip/**"; the exclude is tested first.
        Assert.Empty(RuleIds("src/skip/B.cs"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Rules_PathThatMatchesIncludeAndDefaultExclude_IsExcluded()
    {
        Assert.Empty(RuleIds("src/obj/B.cs"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Rules_RuleWithSeverityOff_NeverApplies()
    {
        Assert.DoesNotContain("switched-off", RuleIds("src/a/B.cs"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void IsOutOfScope_PathNoRuleLooksAt_IsTrue()
    {
        Assert.True(s_scope.IsOutOfScope("other/B.cs"));
        Assert.False(s_scope.IsOutOfScope("src/a/B.cs"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ParametersFor_FileWithoutMatchingOverride_UsesTheRuleValue()
    {
        Assert.Equal(10, MaxLines("src/a/B.cs"));
        Assert.Equal(10, MaxLines("lib/B.cs"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ParametersFor_FileMatchingOneOverride_UsesThatOverride()
    {
        Assert.Equal(30, MaxLines("src/big/B.cs"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ParametersFor_FileMatchingSeveralOverrides_TheLastOneWins()
    {
        // src/a/K.cu.cs matches the first (20) and the third (40) override; src/big/K.cu.cs matches all three.
        Assert.Equal(40, MaxLines("src/a/K.cu.cs"));
        Assert.Equal(40, MaxLines("src/big/K.cu.cs"));
    }
}
