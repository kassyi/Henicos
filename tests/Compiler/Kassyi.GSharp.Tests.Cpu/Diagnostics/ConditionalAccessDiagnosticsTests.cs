using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A null-conditional access on a <c>Nullable&lt;T&gt;</c> whose value the transpiler cannot read without its type
/// (an indexer of the value, a member or element of an array view, a member of a vector) is GSH0003 at the whole
/// access, where the C++ build otherwise stops at a C++ error; a field of the value is written as usual.
/// </summary>
[Category(TestCategory.Compiler)]
public class ConditionalAccessDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ConditionalAccessUnsupported.cu.cs";

    private static readonly string[] s_problems =
    [
        ConditionalAccess.NullableElementAccess,
        ConditionalAccess.NullableArrayView,
        ConditionalAccess.NullableVector
    ];

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ConditionalAccess_UnsupportedNullableReceivers_ReportGsh0003AtEachAccessOnly(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        var actual = diagnostics
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .Select(static d => (d.Origin, d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn,
                Problem: Array.Find(s_problems, p => d.Message.Contains(p, StringComparison.Ordinal)) ?? d.Message))
            .ToArray();
        var id = GSharpDiagnostics.UnsupportedSyntax.Id;
        const DiagnosticOrigin Analyzer = DiagnosticOrigin.Analyzer;
        Assert.Equal(
            [
                (Analyzer, id, 22, 18, 22, 23, ConditionalAccess.NullableElementAccess),
                (Analyzer, id, 34, 22, 34, 32, ConditionalAccess.NullableArrayView),
                (Analyzer, id, 35, 22, 35, 29, ConditionalAccess.NullableArrayView),
                (Analyzer, id, 45, 23, 45, 34, ConditionalAccess.NullableArrayView),
                (Analyzer, id, 56, 20, 56, 24, ConditionalAccess.NullableVector),
                (Analyzer, id, 57, 20, 57, 24, ConditionalAccess.NullableVector)
            ],
            actual);
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
