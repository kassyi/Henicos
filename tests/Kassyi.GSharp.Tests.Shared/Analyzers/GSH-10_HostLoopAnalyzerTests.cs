namespace Kassyi.GSharp.Tests.Analyzers;

using Kassyi.GSharp.Compiler.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;

public class GSH_10_HostLoopAnalyzerTests : TranspilerTestBase
{
    private static readonly HostLoopAnalyzer s_analyzer = new();

    [Theory]
    [Trait("Category", "SadPath")]
    [InlineData("GSH-10", "Analyzers/HostLoop/GSH-10_AutoAllocateReturnInLoop_HostSide.cs", "GSH0015")]
    public async Task Test_HostLoopAnalyzer_ReportsExpectedWarning(string featureId, string testCasePath, string expectedErrorId)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .ReportsErrorsAsync(expectedErrorId);
    }
}
