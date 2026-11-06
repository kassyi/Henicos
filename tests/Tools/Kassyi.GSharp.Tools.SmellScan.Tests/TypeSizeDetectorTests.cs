#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class TypeSizeDetectorTests
{
    private const string Cases = "TypeSizeCases.cs";
    private static readonly TypeSizeDetector s_detector = new();

    private static Measurement Lines(string symbol) =>
        DetectorHarness.Single(s_detector, Cases, symbol, TypeSizeDetector.CodeLinesMetric);

    private static Measurement Members(string symbol) =>
        DetectorHarness.Single(s_detector, Cases, symbol, TypeSizeDetector.MembersMetric);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_SmallClass_ReportsCodeLinesAndMembersWithTheWholeDeclarationAsRange()
    {
        var lines = Lines("Cases.Tiny");
        var members = Members("Cases.Tiny");

        Assert.Equal(4, lines.Value);
        Assert.Equal(4, lines.PhysicalLines);
        Assert.Equal(new SourceRange(3, 1, 6, 2), lines.Range);
        Assert.Equal(1, members.Value);
        Assert.Equal(new SourceRange(3, 1, 6, 2), members.Range);
        Assert.Null(lines.Group);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_EveryKindOfMember_IsCountedOnceAndNestedTypesNotAtAll()
    {
        // A, B, C, Changed1, Changed2, the constructor, the property, the indexer, the operator and the method
        Assert.Equal(10, Members("Cases.Mixed").Value);
        Assert.Equal(14, Lines("Cases.Mixed").Value);
        Assert.Equal(2, Members("Cases.Mixed.NestedClass").Value);
        Assert.Equal(new SourceRange(24, 5, 24, 61), Lines("Cases.Mixed.NestedClass").Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_InterfaceStructRecordAndGenericClass_AreMeasuredLikeClasses()
    {
        Assert.Equal(5, Lines("Cases.IShape").Value);
        Assert.Equal(2, Members("Cases.IShape").Value);
        Assert.Equal(2, Members("Cases.Point").Value);
        Assert.Equal(4, Lines("Cases.Person").Value);
        Assert.Equal(1, Members("Cases.Person").Value);
        Assert.Equal(1, Lines("Cases.Pair").Value);
        Assert.Equal(0, Members("Cases.Pair").Value);
        Assert.Equal(4, Lines("Cases.Box<T>").Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Measure_EnumsAndDelegates_AreNotMeasured()
    {
        Assert.Equal(0, DetectorHarness.Count(s_detector, Cases, "Cases.Colors"));
        Assert.Equal(0, DetectorHarness.Count(s_detector, Cases, "Cases.TopDelegate"));
        Assert.Equal(0, DetectorHarness.Count(s_detector, Cases, "Cases.Mixed.NestedEnum"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_NestedType_IsPartOfTheOuterLinesButNotOfItsMembers()
    {
        Assert.Equal(8, Lines("Cases.Outer").Value);
        Assert.Equal(1, Members("Cases.Outer").Value);
        Assert.Equal(4, Lines("Cases.Outer.Inner").Value);
        Assert.Equal(1, Members("Cases.Outer.Inner").Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_ExtensionBlock_BelongsToTheStaticClassThatHoldsIt()
    {
        Assert.Equal(3, Members("Cases.Extras").Value);
        Assert.Equal(9, Lines("Cases.Extras").Value);
        foreach (var measurement in DetectorHarness.Measure(s_detector, Cases))
        {
            Assert.DoesNotContain("extension", measurement.Symbol);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Judge_LinesEqualToThreshold_AreNotReportedButOneMoreIs()
    {
        // Five has exactly 5 code lines
        var atFive = DetectorHarness.Judge(s_detector, Cases, (TypeSizeDetector.MaxCodeLines, 5));
        var atFour = DetectorHarness.Judge(s_detector, Cases, (TypeSizeDetector.MaxCodeLines, 4));

        Assert.DoesNotContain(atFive, static f => f.Measurement.Symbol == "Cases.Five");
        Assert.Contains(atFour, static f => f.Measurement.Symbol == "Cases.Five");
        Assert.DoesNotContain(atFour, static f => f.Measurement.Symbol == "Cases.Tiny");
        Assert.Contains(
            DetectorHarness.Judge(s_detector, Cases, (TypeSizeDetector.MaxCodeLines, 3)),
            static f => f.Measurement.Symbol == "Cases.Tiny");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Judge_MembersEqualToThreshold_AreNotReportedButOneMoreIs()
    {
        var atTen = DetectorHarness.Judge(s_detector, Cases, (TypeSizeDetector.MaxMembers, 10));
        var atNine = DetectorHarness.Judge(s_detector, Cases, (TypeSizeDetector.MaxMembers, 9));

        Assert.DoesNotContain(atTen, static f => f.Measurement.Symbol == "Cases.Mixed" && f.Measurement.Metric == "members");
        var mixed = Assert.Single(atNine, static f => f.Measurement.Symbol == "Cases.Mixed" && f.Measurement.Metric == "members");
        Assert.Equal(9, mixed.Threshold);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Judge_MembersThresholdZero_TurnsTheMemberCheckOffAndKeepsTheLineCheck()
    {
        var findings = DetectorHarness.Judge(
            s_detector, Cases, (TypeSizeDetector.MaxMembers, 0), (TypeSizeDetector.MaxCodeLines, 5));

        Assert.DoesNotContain(findings, static f => f.Measurement.Metric == "members");
        Assert.Contains(findings, static f => f.Measurement.Symbol == "Cases.Mixed" && f.Measurement.Metric == "codeLines");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_PartialType_CarriesTheGroupNameWithTheNumberOfTypeParameters()
    {
        var split = DetectorHarness.Single(s_detector, "PartialAlphaA.cs", "Cases.Split", TypeSizeDetector.CodeLinesMetric);
        var generic = DetectorHarness.Single(s_detector, "PartialAlphaA.cs", "Cases.Gen<T>", TypeSizeDetector.CodeLinesMetric);
        var renamed = DetectorHarness.Single(s_detector, "PartialAlphaB.cs", "Cases.Gen<TItem>", TypeSizeDetector.CodeLinesMetric);

        Assert.Equal("Cases.Split", split.Group);
        Assert.Equal("Cases.Gen`1", generic.Group);
        Assert.Equal(generic.Group, renamed.Group);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Detector_DeclaresBothThresholdsWithDefaultsAndMinimums()
    {
        Assert.Equal(2, s_detector.Parameters.Length);
        Assert.Equal(new DetectorParameter("max_code_lines", 400, 1), s_detector.Parameters[0]);
        Assert.Equal(new DetectorParameter("max_members", 30, 0), s_detector.Parameters[1]);
    }
}
