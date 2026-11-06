#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

/// <summary>Declaration kinds and name forms that the main detector tests do not cover.</summary>
public class SecondaryDetectorTests
{
    private const string Secondary = "SecondaryCases.cs";
    private static readonly MethodLengthDetector s_length = new();
    private static readonly NestingDepthDetector s_depth = new();

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void MethodLength_IndexerAndEvent_AreMeasuredAsWholeDeclarations()
    {
        Assert.Equal(5, DetectorHarness.Single(s_length, Secondary, "Cases.SecondaryCases.this[int,int]").Value);
        Assert.Equal(5, DetectorHarness.Single(s_length, Secondary, "Cases.SecondaryCases.Changed").Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void NestingDepth_ForeachWithDeconstruction_CountsAsALevel()
    {
        Assert.Equal(1, DetectorHarness.Single(s_depth, Secondary, "Cases.SecondaryCases.Deconstruct()").Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Symbol_MethodTypeParameters_AreInTheNameSoOverloadsDiffer()
    {
        var generic = DetectorHarness.Single(s_length, Secondary, "Cases.SecondaryCases.Generic<T>(int)");
        var plain = DetectorHarness.Single(s_length, Secondary, "Cases.SecondaryCases.Generic(int)");

        Assert.NotEqual(generic.Range, plain.Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Symbol_ExtensionBlocks_AreToldApartByTheirReceiverType()
    {
        var first = DetectorHarness.Single(
            s_length, "ExtensionBlockCases.cs", "Cases.ExtensionBlockCases.extension(string).Length2()");
        var second = DetectorHarness.Single(
            s_length, "ExtensionBlockCases.cs", "Cases.ExtensionBlockCases.extension(int).Length2()");

        Assert.NotEqual(first.UnitPosition, second.UnitPosition);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void MethodLength_TopLevelStatements_AreOneUnit()
    {
        var measurement = DetectorHarness.Single(s_length, "TopLevelCases.cs", "<top-level>");

        Assert.Equal(18, measurement.Value);
        Assert.Equal(20, measurement.PhysicalLines);
        Assert.Equal(new SourceRange(3, 1, 22, 2), measurement.Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void NestingDepth_TopLevelStatements_AreOneUnitWithTheDeepestStatementReported()
    {
        var measurement = DetectorHarness.Single(s_depth, "TopLevelCases.cs", "<top-level>");

        Assert.Equal(4, measurement.Value);
        Assert.Equal(new SourceRange(10, 13, 13, 14), measurement.Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void MethodLength_LocalFunctionAmongTopLevelStatements_IsMeasuredOnItsOwn()
    {
        Assert.Equal(3, DetectorHarness.Single(s_length, "TopLevelCases.cs", "<top-level>.Local()").Value);
    }
}
