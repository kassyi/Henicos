using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// An unfused StackLinq query with a lambda stage stored in a local is not a struct the device lays out, so GSH0078
/// does not report the delegate field of its C# enumerator (#1086).
/// </summary>
[Category(TestCategory.Compiler)]
public class StackLinqLocalLayoutDiagnosticsTests
{
    private const string TestCase = "Diagnostics/StackLinqLocalLayout.cs";

    // The queries of lines 15 and 17 are not reported; the local of a struct with a string field (line 19) still is
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task LocalStackLinqQueries_LocalQueryPipelines_AreNotReportedByGsh0078(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 19, 13, 19, 18)],
            diagnostics.Ranges(GSharpDiagnostics.StructLayoutNotRepresentable.Id));
    }
}
