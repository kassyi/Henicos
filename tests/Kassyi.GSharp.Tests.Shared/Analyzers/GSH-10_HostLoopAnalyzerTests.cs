namespace Kassyi.GSharp.Tests.Analyzers;

using Kassyi.GSharp.Compiler.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;

public class GSH_10_HostLoopAnalyzerTests : TranspilerTestBase
{
    private static readonly HostLoopAnalyzer s_analyzer = new();

    [Theory]
    [Trait("FeatureId", "GSH-10")]
    [Trait("Category", "SadPath")]
    [InlineData("GSH-10", "Analyzers/HostLoop/GSH-10_AutoAllocateReturnInLoop_HostSide.cs", "GSH0015")]
    [InlineData("GSH-10", "Analyzers/HostLoop/GSH-10_DeconstructionLoop_SadPath.cs", "GSH0015")]
    public async Task Test_GSH10_HostLoopAnalyzer_ReportsExpectedWarning(string featureId, string testCasePath, string expectedErrorId)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .ReportsDiagnosticsAsync(expectedErrorId);
    }

    [Theory]
    [Trait("FeatureId", "GSH-10")]
    [Trait("Category", "SafePath")]
    [InlineData("GSH-10", "Analyzers/HostLoop/GSH-10_AutoAllocateReturn_SafePath.cs")]
    public async Task Test_GSH10_HostLoopAnalyzer_ReportsNoWarnings(string featureId, string testCasePath)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }
}
