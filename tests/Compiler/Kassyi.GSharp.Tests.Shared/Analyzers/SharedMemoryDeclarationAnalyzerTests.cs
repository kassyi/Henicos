using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class SharedMemoryDeclarationAnalyzerTests : TranspilerTestBase
{
    private static readonly SharedMemoryDeclarationAnalyzer s_analyzer = new();

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("SharedMemorySameLineKernels.cu.cs")]
    [InlineData("SharedMemorySameLineOverloadKernels.cu.cs")]
    public async Task Test_SharedMemoryDeclarationsOnOneLine_ReportsGSH0065(string testCase)
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/SharedMemoryDeclaration/" + testCase, s_analyzer)
                               .ReportsDiagnosticsAsync("GSH0065");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_SharedMemoryDeclarationsWithDistinctKeys_HasNoDiagnostics()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/SharedMemoryDeclaration/SharedMemoryDistinctKeyKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }
}
