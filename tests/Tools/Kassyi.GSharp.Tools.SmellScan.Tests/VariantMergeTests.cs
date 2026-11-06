#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

/// <summary>A member that differs between <c>#if</c> variants is one finding, not one per variant.</summary>
public class VariantMergeTests
{
    private const string Config = "Config/low-thresholds.yml";

    private static List<Finding> Findings(string caseFile, string rule)
    {
        using var workspace = new ScanWorkspace();
        workspace.Add(caseFile, "src/Case.cs");
        var found = new List<Finding>();
        foreach (var finding in workspace.Scan(Config, 1, "src/Case.cs").Findings)
        {
            if (finding.RuleId == rule)
            {
                found.Add(finding);
            }
        }

        return found;
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Scan_DeepestStatementDiffersBetweenVariants_IsOneFindingWithTheLargestDepth()
    {
        var findings = Findings("VariantNestingCases.cs", "deep-nesting");

        var finding = Assert.Single(findings);
        Assert.Equal(4, finding.Measurement.Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Scan_AttributeInsideIf_IsOneFindingWithTheLargestLength()
    {
        var findings = Findings("VariantAttributeCases.cs", "long-method");

        var finding = Assert.Single(findings, f => f.Measurement.Symbol.Contains(".M()"));
        Assert.Equal(5, finding.Measurement.Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Scan_ParameterTypeChangesInsideIf_IsOneFinding()
    {
        var findings = Findings("VariantParameterTypeCases.cs", "long-method");

        Assert.Single(findings, f => f.Measurement.Symbol.Contains(".P("));
    }
}
