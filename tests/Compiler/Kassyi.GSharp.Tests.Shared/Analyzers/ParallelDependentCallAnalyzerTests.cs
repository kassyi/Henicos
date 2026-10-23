namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class ParallelDependentCallAnalyzerTests : TranspilerTestBase
{
    private static readonly ParallelDependentCallAnalyzer s_analyzer = new();

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("GSH-35-Sad", "Analyzers/ParallelDependentCall_SadPath.cu.txt", new[] { "GSH0035", "GSH0035", "GSH0035", "GSH0035", "GSH0035" })]
    public async Task Test_ParallelDependentCallAnalyzer_ReportsMultipleExpectedDiagnostics(string featureId, string testCasePath, string[] expectedDiagnosticIds)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .ReportsDiagnosticsAsync(expectedDiagnosticIds);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("GSH-35-Happy", "Analyzers/ParallelDependentCall_HappyPath.cu.txt")]
    public async Task Test_ParallelDependentCallAnalyzer_HasNoErrors(string featureId, string testCasePath)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .HasNoErrorsAsync();
    }
}
