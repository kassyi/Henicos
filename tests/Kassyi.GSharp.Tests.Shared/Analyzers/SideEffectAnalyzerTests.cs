using System.Threading.Tasks;
using Xunit;
using Kassyi.GSharp.Compiler.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class SideEffectAnalyzerTests : AnalyzerTestBase
{
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public async Task HappyPath_NoDiagnostics()
    {
        await VerifyAnalyzerFromFileAsync(
            "SideEffectAnalyzer_HappyPath.cs",
            new SideEffectAnalyzer(),
            "SideEffectAnalyzer_HappyPath");
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task SadPath_ReportsDiagnostics()
    {
        await VerifyAnalyzerFromFileAsync(
            "SideEffectAnalyzer_SadPath.cs",
            new SideEffectAnalyzer(),
            "SideEffectAnalyzer_SadPath");
    }
}
