using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A null-conditional access on a <c>Nullable&lt;T&gt;</c> whose value the transpiler cannot read without its type
/// (an indexer of the value, a member or element of an array view) is GSH0003 at the whole access, where the C++
/// build otherwise stops at a C++ error; a field of the value is written as usual.
/// </summary>
[Category(TestCategory.Compiler)]
public class ConditionalAccessDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ConditionalAccessUnsupported.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ConditionalAccess_IndexerOfANullableStruct_ReportsGsh0003AtTheAccess(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 22, 18, 22, 23)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, ConditionalAccess.NullableElementAccess));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ConditionalAccess_NullableArrayView_ReportsGsh0003AtEachAccess(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (DiagnosticOrigin.Analyzer, 34, 22, 34, 32),
                (DiagnosticOrigin.Analyzer, 35, 22, 35, 29),
                (DiagnosticOrigin.Analyzer, 45, 23, 45, 34)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, ConditionalAccess.NullableArrayView));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ConditionalAccess_UnsupportedReceivers_ReportNothingElse(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(4, diagnostics.Length);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ConditionalAccess_GeneratedSource_StatesEachFailureWhereItIsReported(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        var stated = KernelDiagnostics.FindStatedFailures(run);
        Assert.NotEmpty(stated);
        Assert.All(stated, static f => Assert.True(f.IsReported, f.Failure));
    }
}
