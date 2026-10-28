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
        // Escaped, DeepChain, LocalCallsLocal and LocalDirect have none
        // mutant: a call to a method the analysis cannot see does not count as Escaped (Escaped is reported)
        // mutant: MaxHelperDepth = 0, so every helper call counts as Escaped (HelperWithoutDispose is not reported)
        // mutant: arguments matched to the next parameter (RefHelper is reported)
        // mutant: x?.Dispose() not counted (ConditionalAccess is reported)
        // mutant: nameof(<field>) treated as a call the analysis cannot see (NameOfOnly is not reported)
        // mutant: a local function looked up in the calling body only (LocalCallsLocal is reported)
        (int Line, string Field)[] expected =
        [
            (125, "_a"), // HelperWithoutDispose
            (136, "_b"), // OneFieldMissing
            (150, "_a"), // HelperCycle
            (238, "_a"), // NameOfOnly
            (249, "_a"), // EmptyDispose
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
    public async Task BorrowedFieldsReleasedThroughHelpers_ReportGSH0027()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer(TestCasePath, s_analyzer).GetDiagnosticsAsync();

        // BorrowedThroughHelper: _c is released through the helper; _d is only passed to Logger.Log, which the analysis
        // cannot see (not a release, so no second report). ShallowerPathAfterDeep and ShallowPathOnly release the
        // borrowed field three helpers deep on their direct path.
        // mutant: GSH0027 reported for an escaped field as well (two reports for BorrowedThroughHelper)
        // mutant: a body already visited on a deeper path is not walked again from a shallower one
        // (ShallowerPathAfterDeep is not reported)
        int[] expectedLines = [176, 196, 221];
        var actual = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.DisposingBorrowedResource.Id)
            .OrderBy(static d => d.Location.GetLineSpan().StartLinePosition.Line)
            .ToArray();

        Assert.Equal(expectedLines.Length, actual.Length);
        for (var i = 0; i < expectedLines.Length; i++)
        {
            var span = actual[i].Location.GetLineSpan();
            Assert.Equal((expectedLines[i], 16), (span.StartLinePosition.Line, span.StartLinePosition.Character));
            Assert.Equal((expectedLines[i], 23), (span.EndLinePosition.Line, span.EndLinePosition.Character));
        }
    }
}
