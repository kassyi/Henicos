#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

/// <summary>The parts of a partial type are added up per project, then judged once and reported at the largest part.</summary>
public class PartialTypeMergeTests
{
    private const string Config = "Config/type-size-low.yml";

    private static ScanWorkspace TwoPartsInOneProject()
    {
        var workspace = new ScanWorkspace();
        workspace.Write("proj/P.csproj", "<Project />");
        workspace.Add("PartialAlphaA.cs", "proj/PartialAlphaA.cs");
        workspace.Add("PartialAlphaB.cs", "proj/PartialAlphaB.cs");
        return workspace;
    }

    private static Finding Find(ScanReport report, string symbol, string metric)
    {
        var matches = new List<Finding>();
        foreach (var finding in report.Findings)
        {
            if (finding.Measurement.Symbol == symbol && finding.Measurement.Metric == metric)
            {
                matches.Add(finding);
            }
        }

        return Assert.Single(matches);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Scan_PartsInTwoFiles_AreAddedUpAndReportedAtTheLargestPart()
    {
        using var workspace = TwoPartsInOneProject();

        var report = workspace.Scan(Config, 1, "proj/PartialAlphaA.cs", "proj/PartialAlphaB.cs");

        var lines = Find(report, "Cases.Split", "codeLines");
        Assert.Equal(11, lines.Measurement.Value);
        Assert.Equal("proj/PartialAlphaA.cs", lines.Measurement.Path);
        Assert.Equal(new SourceRange(3, 1, 8, 2), lines.Measurement.Range);
        var related = Assert.Single(lines.RelatedLocations);
        Assert.Equal("proj/PartialAlphaB.cs", related.Path);
        Assert.Equal(new SourceRange(3, 1, 7, 2), related.Range);
        Assert.Equal("Another part of Cases.Split", related.Message);

        var members = Find(report, "Cases.Split", "members");
        Assert.Equal(5, members.Measurement.Value);
        Assert.Equal(4, members.Threshold);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Scan_RenamedTypeParameter_StillMakesOneUnitAndTheLargerPartIsReported()
    {
        using var workspace = TwoPartsInOneProject();

        var report = workspace.Scan(Config, 1, "proj/PartialAlphaA.cs", "proj/PartialAlphaB.cs");

        // Gen<T> has 4 code lines in A and Gen<TItem> has 5 in B; 9 is above 8
        var gen = Find(report, "Cases.Gen<TItem>", "codeLines");
        Assert.Equal(9, gen.Measurement.Value);
        Assert.Equal("proj/PartialAlphaB.cs", gen.Measurement.Path);
        Assert.Equal("proj/PartialAlphaA.cs", Assert.Single(gen.RelatedLocations).Path);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Scan_SumEqualToThreshold_IsNotReportedButOneMoreIs()
    {
        using var workspace = TwoPartsInOneProject();

        // Split has 11 code lines in total; a limit of 11 is not exceeded, a limit of 10 is
        var atEleven = workspace.Scan("Config/type-size-eleven.yml", 1, "proj/PartialAlphaA.cs", "proj/PartialAlphaB.cs");
        var atTen = workspace.Scan("Config/type-size-ten.yml", 1, "proj/PartialAlphaA.cs", "proj/PartialAlphaB.cs");

        Assert.DoesNotContain(atEleven.Findings, static f => f.Measurement.Symbol == "Cases.Split");
        Assert.Contains(atTen.Findings, static f => f.Measurement.Symbol == "Cases.Split");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Scan_PartsInDifferentProjects_AreNotAddedUp()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("one/P.csproj", "<Project />");
        workspace.Write("two/P.csproj", "<Project />");
        workspace.Add("PartialAlphaA.cs", "one/PartialAlphaA.cs");
        workspace.Add("PartialAlphaB.cs", "two/PartialAlphaB.cs");

        var report = workspace.Scan(Config, 1, "one/PartialAlphaA.cs", "two/PartialAlphaB.cs");

        Assert.DoesNotContain(report.Findings, static f => f.Measurement.Symbol == "Cases.Split");
        Assert.DoesNotContain(report.Findings, static f => f.Measurement.Symbol.StartsWith("Cases.Gen", StringComparison.Ordinal));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Scan_TwoPartsInOneFile_AreAddedUpToo()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("proj/P.csproj", "<Project />");
        workspace.Add("PartialTwiceInOneFile.cs", "proj/PartialTwiceInOneFile.cs");

        var report = workspace.Scan(Config, 1, "proj/PartialTwiceInOneFile.cs");

        // 4 and 5 code lines; the larger second part is where the finding sits
        var twice = Find(report, "Cases.Twice", "codeLines");
        Assert.Equal(9, twice.Measurement.Value);
        Assert.Equal(new SourceRange(8, 1, 12, 2), twice.Measurement.Range);
        Assert.Equal(new SourceRange(3, 1, 6, 2), Assert.Single(twice.RelatedLocations).Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Scan_PartsOfEqualSize_AreReportedAtThePathThatSortsFirst()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("proj/P.csproj", "<Project />");
        workspace.Add("PartialTie.cs", "proj/b.cs");
        workspace.Add("PartialTie.cs", "proj/a.cs");

        var report = workspace.Scan(Config, 1, "proj/b.cs", "proj/a.cs");

        var tie = Find(report, "Cases.Tie", "codeLines");
        Assert.Equal(12, tie.Measurement.Value);
        Assert.Equal("proj/a.cs", tie.Measurement.Path);
        Assert.Equal("proj/b.cs", Assert.Single(tie.RelatedLocations).Path);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Scan_EqualPartsInOneFile_AreReportedAtTheFirstOne()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("proj/P.csproj", "<Project />");
        workspace.Add("PartialTiedInOneFile.cs", "proj/PartialTiedInOneFile.cs");

        var report = workspace.Scan(Config, 1, "proj/PartialTiedInOneFile.cs");

        var tied = Find(report, "Cases.Tied", "codeLines");
        Assert.Equal(10, tied.Measurement.Value);
        Assert.Equal(new SourceRange(3, 1, 7, 2), tied.Measurement.Range);
        Assert.Equal(new SourceRange(9, 1, 13, 2), Assert.Single(tied.RelatedLocations).Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Scan_OneMetricOfATypeAppearingOrGoing_DoesNotChangeTheFingerprintOfTheOther()
    {
        using var membersOnly = new ScanWorkspace();
        membersOnly.Add("TypeSizeCases.cs", "proj/TypeSizeCases.cs");
        using var both = new ScanWorkspace();
        both.Add("TypeSizeCases.cs", "proj/TypeSizeCases.cs");

        // Mixed has 14 code lines and 10 members: the first run reports only the members, the second the lines as well
        var first = Find(membersOnly.Scan("Config/type-size-members-only.yml", 1, "proj/TypeSizeCases.cs"), "Cases.Mixed", "members");
        var secondReport = both.Scan(Config, 1, "proj/TypeSizeCases.cs");
        var second = Find(secondReport, "Cases.Mixed", "members");

        Assert.NotEqual(Find(secondReport, "Cases.Mixed", "codeLines").Fingerprint, second.Fingerprint);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Scan_MovingAPartToAnotherFile_KeepsTheFingerprint()
    {
        using var before = TwoPartsInOneProject();
        using var after = new ScanWorkspace();
        after.Write("proj/P.csproj", "<Project />");
        after.Add("PartialAlphaA.cs", "proj/Renamed.cs");
        after.Add("PartialAlphaB.cs", "proj/PartialAlphaB.cs");

        var first = Find(before.Scan(Config, 1, "proj/PartialAlphaA.cs", "proj/PartialAlphaB.cs"), "Cases.Split", "codeLines");
        var second = Find(after.Scan(Config, 1, "proj/Renamed.cs", "proj/PartialAlphaB.cs"), "Cases.Split", "codeLines");

        Assert.Equal("proj/Renamed.cs", second.Measurement.Path);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Scan_LinesAndMembersOfOneType_AreTwoFindingsWithDifferentFingerprints()
    {
        using var workspace = TwoPartsInOneProject();

        var report = workspace.Scan(Config, 1, "proj/PartialAlphaA.cs", "proj/PartialAlphaB.cs");

        var lines = Find(report, "Cases.Split", "codeLines");
        var members = Find(report, "Cases.Split", "members");
        Assert.NotEqual(lines.Fingerprint, members.Fingerprint);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Scan_PartsWhoseFilesAreOutOfScopeOfTheRule_AreNotAddedUp()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("proj/P.csproj", "<Project />");
        workspace.Add("PartialAlphaA.cs", "proj/PartialAlphaA.cs");
        workspace.Add("PartialAlphaB.cs", "proj/PartialAlphaB.cs");

        // The rule looks only at the first file, so the second part does not count
        var report = workspace.Scan("Config/type-size-first-only.yml", 1, "proj/PartialAlphaA.cs", "proj/PartialAlphaB.cs");

        Assert.DoesNotContain(report.Findings, static f => f.Measurement.Symbol == "Cases.Split");
    }
}
