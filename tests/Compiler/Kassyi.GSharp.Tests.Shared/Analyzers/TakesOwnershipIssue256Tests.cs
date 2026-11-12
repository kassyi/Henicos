using System.Threading.Tasks;
using Xunit;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.GSharp.Compiler.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class TakesOwnershipIssue256Tests : TranspilerTestBase
{
    private static readonly DisposableOwnershipAnalyzer s_ownershipAnalyzer = new();
    private const string TestCasePath = "Analyzers/TakesOwnershipIssue256.cs";

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task TakesOwnership_MissingIDisposableOnPrimaryStruct_ReportsDiagnostic()
    {
        await GeneratorTestRunner.TestAnalyzer(TestCasePath, s_ownershipAnalyzer)
            .ReportsErrorsAsync(GSharpDiagnostics.MissingIDisposable);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task TakesOwnership_MissingIDisposableOnStandardClass_ReportsDiagnostic()
    {
        await GeneratorTestRunner.TestAnalyzer(TestCasePath, s_ownershipAnalyzer)
            .ReportsErrorsAsync(GSharpDiagnostics.MissingIDisposable);
    }
}
