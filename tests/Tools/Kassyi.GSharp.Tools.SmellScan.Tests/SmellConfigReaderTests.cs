#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class SmellConfigReaderTests
{
    private static SmellConfig Read(string name) => SmellConfigReader.Read(name, TestCaseFiles.Read("Config/" + name));

    private static SmellConfigException Fail(string name) =>
        Assert.Throws<SmellConfigException>(() => Read(name));

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Read_ValidFile_ReturnsDefaultsRulesAndOverridesInFileOrder()
    {
        var config = Read("valid.yml");

        Assert.Equal(["**/obj/**", "generated/**"], TestValues.Strings(config.DefaultExclude));
        Assert.Equal(2, config.Rules.Length);

        var rule = config.Rules[0];
        Assert.Equal("long-method", rule.Id);
        Assert.Equal("MethodLength", rule.Detector.Name);
        Assert.Equal(SmellSeverity.Warning, rule.Severity);
        Assert.Equal(10, rule.Parameters["max_code_lines"]);
        Assert.Equal(["src/**", "lib/**"], TestValues.Strings(rule.Include));
        Assert.Equal(["src/skip/**"], TestValues.Strings(rule.Exclude));
        Assert.Equal("Extract Method.", rule.Suggestion);
        Assert.Equal("docs/long-method.md", rule.Help);
        Assert.Equal(8, rule.Line);
        Assert.Equal(3, rule.Overrides.Length);
        Assert.Equal(20, rule.Overrides[0].Parameters["max_code_lines"]);
        Assert.Equal(30, rule.Overrides[1].Parameters["max_code_lines"]);
        Assert.Equal(40, rule.Overrides[2].Parameters["max_code_lines"]);

        Assert.Equal(SmellSeverity.Off, config.Rules[1].Severity);
        Assert.Empty(config.Rules[1].Include);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_UnknownDetector_ReportsTheLineOfTheDetectorAndTheKnownNames()
    {
        var error = Fail("unknown-detector.yml");

        Assert.Equal(7, error.Line);
        Assert.StartsWith("unknown-detector.yml:7: ", error.Message);
        Assert.Contains("NoSuchDetector", error.Message);
        Assert.Contains("MethodLength, ParameterCount, NestingDepth", error.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_UnknownSeverity_ReportsTheLineOfTheSeverity()
    {
        var error = Fail("bad-severity.yml");

        Assert.Equal(5, error.Line);
        Assert.Contains("loud", error.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_ParameterTheDetectorDoesNotDeclare_ReportsTheLineOfTheKey()
    {
        var error = Fail("unknown-parameter.yml");

        Assert.Equal(8, error.Line);
        Assert.Contains("max_lines", error.Message);
        Assert.Contains("max_code_lines", error.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_ParameterThatIsNotAnInteger_ReportsTheLineOfTheValue()
    {
        var error = Fail("non-integer.yml");

        Assert.Equal(7, error.Line);
        Assert.Contains("many", error.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Read_OverrideParameterBelowTheMinimum_ReportsTheLineOfTheValue()
    {
        var error = Fail("below-min.yml");

        Assert.Equal(9, error.Line);
        Assert.Contains("at least 1", error.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_DuplicateRuleId_ReportsTheSecondRuleAndTheFirstLine()
    {
        var error = Fail("duplicate-id.yml");

        Assert.Equal(6, error.Line);
        Assert.Contains("already used at line 3", error.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_RuleWithoutDetector_ReportsTheLineWhereTheRuleStarts()
    {
        var error = Fail("missing-detector.yml");

        Assert.Equal(3, error.Line);
        Assert.Contains("'detector'", error.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_UnknownKey_ReportsTheLineOfTheKey()
    {
        var error = Fail("unknown-key.yml");

        Assert.Equal(6, error.Line);
        Assert.Contains("treshold", error.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_UnsupportedVersion_ReportsTheLineOfTheVersion()
    {
        var error = Fail("bad-version.yml");

        Assert.Equal(1, error.Line);
        Assert.Contains("version 2", error.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_YamlSyntaxError_IsReportedWithALineInsideTheBrokenRule()
    {
        var error = Fail("syntax-error.yml");

        Assert.InRange(error.Line, 4, 6);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_QuotedInteger_IsATypeErrorOnTheLineOfTheValue()
    {
        var error = Fail("quoted-integer.yml");

        Assert.Equal(7, error.Line);
        Assert.Contains("quoted", error.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_UnterminatedFlowList_IsAConfigurationErrorNotACrash()
    {
        var error = Fail("unterminated-flow.yml");

        Assert.Contains("cannot be read", error.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_RuleIdThatIsNotKebabCase_ReportsTheLineOfTheId()
    {
        var error = Fail("id-not-kebab.yml");

        Assert.Equal(3, error.Line);
        Assert.Contains("kebab-case", error.Message);
    }

    [Theory]
    [InlineData("long-method", true)]
    [InlineData("a", true)]
    [InlineData("a1-b2", true)]
    [InlineData("-a", false)]
    [InlineData("a-", false)]
    [InlineData("a--b", false)]
    [InlineData("1a", false)]
    [InlineData("A", false)]
    [InlineData("a_b", false)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Read_RuleIdShape_AcceptsOnlyKebabCase(string id, bool accepted)
    {
        var text = "version: 1\nrules:\n  - id: " + id + "\n    detector: MethodLength\n    severity: warning\n";

        if (accepted)
        {
            Assert.Equal(id, SmellConfigReader.Read("inline.yml", text).Rules[0].Id);
        }
        else
        {
            Assert.Equal(3, Assert.Throws<SmellConfigException>(() => SmellConfigReader.Read("inline.yml", text)).Line);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_OverrideWithoutFiles_ReportsTheLineOfTheFilesKey()
    {
        var error = Fail("empty-override-files.yml");

        Assert.Equal(7, error.Line);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_RulesThatIsNotAList_ReportsTheLineOfTheMapping()
    {
        var error = Fail("rules-not-list.yml");

        Assert.Equal(3, error.Line);
        Assert.Contains("list", error.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_EmptyText_ReportsLineOne()
    {
        var error = Assert.Throws<SmellConfigException>(() => SmellConfigReader.Read("empty.yml", string.Empty));

        Assert.Equal(1, error.Line);
    }
}
