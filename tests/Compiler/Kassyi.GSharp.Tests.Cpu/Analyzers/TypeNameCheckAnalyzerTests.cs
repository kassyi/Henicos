using Kassyi.GSharp.Tests.Generators;
using Kassyi.GSharp.Tests.Infrastructure;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class TypeNameCheckAnalyzerTests : AnalyzerTestBase
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task TypeNameCheck_DisallowedTypeNamePattern_ReportsWarning()
    {
        await VerifyAnalyzerFromFileAsync(
            "TypeNameCheck/NameOnlyChecks.cs",
            new TypeNameCheckAnalyzer(),
            nameof(TypeNameCheck_DisallowedTypeNamePattern_ReportsWarning));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task TypeNameCheck_AllowedTypeConstants_ProducesNoDiagnostics()
    {
        await VerifyAnalyzerFromFileAsync(
            "TypeNameCheck/WithoutTypeConstants.cs",
            new TypeNameCheckAnalyzer(),
            nameof(TypeNameCheck_AllowedTypeConstants_ProducesNoDiagnostics));
    }
}
