using Kassyi.GSharp.Tests.Generators;
using Kassyi.GSharp.Tests.Infrastructure;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class TypeNameCheckAnalyzerTests : AnalyzerTestBase
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_NameOnlyChecks_ReportsWarning()
    {
        await VerifyAnalyzerFromFileAsync("TypeNameCheck/NameOnlyChecks.cs", new TypeNameCheckAnalyzer(), nameof(Test_NameOnlyChecks_ReportsWarning));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_WithoutTypeConstants_NoDiagnostics()
    {
        await VerifyAnalyzerFromFileAsync("TypeNameCheck/WithoutTypeConstants.cs", new TypeNameCheckAnalyzer(), nameof(Test_WithoutTypeConstants_NoDiagnostics));
    }
}
