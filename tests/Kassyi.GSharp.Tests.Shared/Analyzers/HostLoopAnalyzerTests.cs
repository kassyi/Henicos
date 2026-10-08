namespace Kassyi.GSharp.Tests.Shared.Analyzers;

using Kassyi.GSharp.Compiler.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Kassyi.GSharp.Tests.Shared.Helpers;

public class HostLoopAnalyzerTests : TranspilerTestBase
{
    private static readonly HostLoopAnalyzer s_analyzer = new();

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("GSH-10", "Analyzers/HostLoop/AutoAllocateReturnInLoop_HostSide.cs", "GSH0015")]
    [InlineData("GSH-10", "Analyzers/HostLoop/DeconstructionLoop_SadPath.cs", "GSH0015")]
    public async Task Test_HostLoopAnalyzer_ReportsExpectedWarning(string featureId, string testCasePath, string expectedErrorId)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .ReportsDiagnosticsAsync(expectedErrorId);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("GSH-10", "Analyzers/HostLoop/AutoAllocateReturn_SafePath.cs")]
    public async Task Test_HostLoopAnalyzer_ReportsNoWarnings(string featureId, string testCasePath)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }
}

