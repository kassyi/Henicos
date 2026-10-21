using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Infrastructure;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class StackLinqChainAnalyzerTests : AnalyzerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_ShortChain_NoDiagnostics()
    {
        await VerifyAnalyzerFromFileAsync("StackLinqChain/ShortChain_HappyPath.cs", new StackLinqChainAnalyzer(), nameof(Test_ShortChain_NoDiagnostics));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_LongChain_ReportsError()
    {
        await VerifyAnalyzerFromFileAsync("StackLinqChain/LongChain_SadPath.cs", new StackLinqChainAnalyzer(), nameof(Test_LongChain_ReportsError));
    }
}
