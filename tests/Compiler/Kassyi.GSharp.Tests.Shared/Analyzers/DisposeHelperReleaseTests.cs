using Kassyi.GSharp.Compiler.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// GSH0022 counts a field as released when the Dispose method releases it through a helper of the same type (#786), and
/// still reports a field that no path releases; GSH0027 reports a borrowed field that a helper releases.
/// </summary>
[Category(TestCategory.Compiler)]
public class DisposeHelperReleaseTests : TranspilerTestBase
{
    private static readonly DisposableOwnershipAnalyzer s_analyzer = new();
    private const string TestCasePath = "Analyzers/DisposeHelperRelease.cs";

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task UnreleasedFields_ReportGSH0022AtDispose()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer(TestCasePath, s_analyzer).GetDiagnosticsAsync();

        // (0-based line of the Dispose identifier, field): RefHelper, ConditionalAccess, NestedHelper, LocalAndValueHelpers,
        // Escaped and DeepChain have none
        // mutant: a call to a method the analysis cannot see does not count as Escaped (Escaped is reported)
        // mutant: MaxHelperDepth = 0, so every helper call counts as Escaped (HelperWithoutDispose is not reported)
        // mutant: arguments matched to the next parameter (RefHelper is reported)
        // mutant: x?.Dispose() not counted (ConditionalAccess is reported)
        (int Line, string Field)[] expected =
        [
            (125, "_a"), // HelperWithoutDispose
            (136, "_b"), // OneFieldMissing
            (150, "_a"), // HelperCycle
        ];
        var actual = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.UndisposedGpuScope.Id)
            .OrderBy(static d => d.Location.GetLineSpan().StartLinePosition.Line)
            .ToArray();

        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            var span = actual[i].Location.GetLineSpan();
            Assert.Equal((expected[i].Line, 16), (span.StartLinePosition.Line, span.StartLinePosition.Character));
            Assert.Equal((expected[i].Line, 23), (span.EndLinePosition.Line, span.EndLinePosition.Character));
            Assert.Contains($"'{expected[i].Field}'", actual[i].GetMessage());
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task BorrowedFieldReleasedThroughHelper_ReportsGSH0027Once()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer(TestCasePath, s_analyzer).GetDiagnosticsAsync();

        // _c is released through the helper; _d is only passed to Logger.Log, which the analysis cannot see (not a
        // release, so no second report)
        // mutant: GSH0027 reported for an escaped field as well (two reports)
        var warning = Assert.Single(diagnostics, static d => d.Id == GSharpDiagnostics.DisposingBorrowedResource.Id);
        var span = warning.Location.GetLineSpan();
        Assert.Equal((176, 16), (span.StartLinePosition.Line, span.StartLinePosition.Character));
        Assert.Equal((176, 23), (span.EndLinePosition.Line, span.EndLinePosition.Character));
    }
}
