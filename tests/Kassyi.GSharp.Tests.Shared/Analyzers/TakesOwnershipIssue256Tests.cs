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
    [Trait("Category", "SadPath")]
    public async Task Test_11_3_X_MissingIDisposableOnTakesOwnership_Primary()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_ownershipAnalyzer)
            .ReportsErrorsAsync("GSH0020");
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public async Task Test_11_3_X_MissingIDisposableOnTakesOwnership_Standard()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_ownershipAnalyzer)
            .ReportsErrorsAsync("GSH0020");
    }
}
