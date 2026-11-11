using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Infrastructure;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class StackLinqChainAnalyzerTests : AnalyzerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task StackLinqChain_ShortChain_ProducesNoDiagnostics()
    {
        await VerifyAnalyzerFromFileAsync(
            "StackLinqChain/ShortChain_HappyPath.cs",
            new StackLinqChainAnalyzer(),
            nameof(StackLinqChain_ShortChain_ProducesNoDiagnostics));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task StackLinqChain_LongChainExceedingLimit_ReportsError()
    {
        await VerifyAnalyzerFromFileAsync(
            "StackLinqChain/LongChain_SadPath.cs",
            new StackLinqChainAnalyzer(),
            nameof(StackLinqChain_LongChainExceedingLimit_ReportsError));
    }
}
