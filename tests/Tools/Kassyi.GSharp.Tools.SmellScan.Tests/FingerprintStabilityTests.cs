#nullable enable
using System.Linq;
using System.Text;

namespace Kassyi.GSharp.Tools.SmellScan.Tests;

/// <summary>
/// A before/after comparison by fingerprint must point at the place that was added when a symbol has several findings of
/// one rule, and must not report anything for edits that leave the findings alone.
/// </summary>
public class FingerprintStabilityTests
{
    private const string Config = "Config/all-detectors.yml";
    private const string Path = "proj/Swallowed.cs";

    private static string Method(params string[] body) =>
        "namespace N;\npublic class C\n{\n    public void M()\n    {\n" + string.Join("\n", body) + "\n    }\n}\n";

    private const string IoCatch = "        try { Work(); } catch (System.IO.IOException) { }";
    private const string ArgumentCatch = "        try { Work(); } catch (System.ArgumentException) { }";
    private const string OperationCatch = "        try { Work(); } catch (System.InvalidOperationException) { }";

    private static ScanReport Scan(ScanWorkspace workspace, params string[] files) => workspace.Scan(Config, 1, files);

    // The new findings of one rule; the type-size rule of the test configuration also reports these small classes
    private static List<Finding> New(ScanReport before, ScanReport after, string rule = "swallowed-exception")
    {
        var added = BaselineFilter.KeepNew(after, Encoding.UTF8.GetString(JsonReportWriter.Write(before)));
        return [.. added.Findings.Where(f => f.RuleId == rule)];
    }

    private static List<Finding> Of(ScanReport report, string rule = "swallowed-exception") =>
        [.. report.Findings.Where(f => f.RuleId == rule)];

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void KeepNew_CatchAddedBetweenTwoOthersInTheSameMethod_ReportsOnlyTheAddedOne()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write(Path, Method(IoCatch, ArgumentCatch));
        var before = Scan(workspace, Path);
        workspace.Write(Path, Method(IoCatch, OperationCatch, ArgumentCatch));
        var after = Scan(workspace, Path);

        var added = New(before, after);

        var finding = Assert.Single(added);
        Assert.Equal(7, finding.Measurement.Range.StartLine);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void KeepNew_OneCatchRemovedAndAnotherAddedElsewhere_ReportsTheAddedOne()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write(Path, Method(IoCatch, ArgumentCatch));
        var before = Scan(workspace, Path);
        workspace.Write(Path, Method(ArgumentCatch, OperationCatch));
        var after = Scan(workspace, Path);

        var added = New(before, after);

        var finding = Assert.Single(added);
        Assert.Equal(7, finding.Measurement.Range.StartLine);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void KeepNew_EditsAroundTheFindingsInTheMethod_ReportNothing()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write(Path, Method(IoCatch, ArgumentCatch));
        var before = Scan(workspace, Path);
        workspace.Write(
            Path,
            Method("        var x = 1;", "        // a comment", IoCatch, "        x++;", ArgumentCatch, "        x--;"));
        var after = Scan(workspace, Path);

        Assert.Equal(2, Of(after).Count);
        Assert.Empty(New(before, after));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void KeepNew_ReformattedCatch_IsTheSameFinding()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write(Path, Method(IoCatch));
        var before = Scan(workspace, Path);
        workspace.Write(Path, Method("        try { Work(); }\n        catch (System.IO.IOException)\n        {\n            // ignored\n        }"));
        var after = Scan(workspace, Path);

        Assert.Single(Of(after));
        Assert.Empty(New(before, after));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void KeepNew_ThirdIdenticalCatch_IsOneNewFinding()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write(Path, Method(IoCatch, IoCatch));
        var before = Scan(workspace, Path);
        workspace.Write(Path, Method(IoCatch, IoCatch, IoCatch));
        var after = Scan(workspace, Path);

        Assert.Single(New(before, after));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void KeepNew_FileMovedToAnotherFolder_ReportsNothing()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("proj/SwallowedA.cs", Method(IoCatch, ArgumentCatch));
        var before = Scan(workspace, "proj/SwallowedA.cs");
        workspace.Write("proj/SwallowedB.cs", Method(IoCatch, ArgumentCatch));
        var after = Scan(workspace, "proj/SwallowedB.cs");

        Assert.Equal(2, Of(after).Count);
        Assert.Empty(New(before, after));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void KeepNew_LongLiteralInAnAssemblyAttribute_AddedInAnEarlierFile_ReportsTheNewFile()
    {
        var literal = new string('x', 130);
        var attribute = "[assembly: System.Reflection.AssemblyDescription(\"" + literal + "\")]\n";
        using var workspace = new ScanWorkspace();
        workspace.Write("src/B.cs", attribute);
        var before = Scan(workspace, "src/B.cs");
        workspace.Write("src/A.cs", attribute.Replace("xx", "yy", StringComparison.Ordinal));
        var after = Scan(workspace, "src/A.cs", "src/B.cs");

        var finding = Assert.Single(New(before, after, "long-string-literal"));

        Assert.Equal("src/A.cs", finding.Measurement.Path);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void KeepNew_IfChainAddedBeforeAnotherInTheSameMethod_ReportsTheAddedChain()
    {
        const string first = "        if (x == 1) { } else if (x == 2) { } else if (x == 3) { } else if (x == 4) { } else if (x == 5) { } else { }";
        const string second = "        if (y == 1) { } else if (y == 2) { } else if (y == 3) { } else if (y == 4) { } else if (y == 5) { } else { }";
        using var workspace = new ScanWorkspace();
        workspace.Write("proj/Chains.cs", Method("        int x = 0, y = 0;", second));
        var before = Scan(workspace, "proj/Chains.cs");
        workspace.Write("proj/Chains.cs", Method("        int x = 0, y = 0;", first, second));
        var after = Scan(workspace, "proj/Chains.cs");

        var finding = Assert.Single(New(before, after, "long-if-chain"));

        Assert.Equal(7, finding.Measurement.Range.StartLine);
    }
}
