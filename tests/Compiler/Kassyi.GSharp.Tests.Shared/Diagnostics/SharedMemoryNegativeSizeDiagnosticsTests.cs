using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// GSH0117: a <c>SharedMemory&lt;T&gt;</c> whose count is a negative C# constant is an error at the count, on every
/// dialect, as C# rejects an array of a negative constant size (CS0248). A count of 0 declares empty shared memory,
/// and a count known only at run time is checked when the kernel runs (#1790).
/// </summary>
[Category(TestCategory.Compiler)]
public class SharedMemoryNegativeSizeDiagnosticsTests
{
    private const string SadPath = "Diagnostics/SharedMemoryNegativeSize.cu.cs";

    // Line 15: a literal. Line 23: a const field through a target-typed new. Line 24: a constant expression. Line 31:
    // int.MinValue in a device function. AcceptedCountsKernel (0, n - 10 and 1) is not reported
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task NegativeConstantCount_EveryForm_ReportsGsh0117AtCount(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(SadPath, dialect);

        Assert.Equal(
            [
                (Analyzer, 15, 39, 15, 41),
                (Analyzer, 23, 37, 23, 50),
                (Analyzer, 24, 40, 24, 49),
                (Analyzer, 31, 39, 31, 51)
            ],
            diagnostics.Ranges(GSharpDiagnostics.SharedMemorySizeNegative.Id));
        Assert.Equal(
            [
                "SharedMemory<T> cannot have a negative count (-1)",
                "SharedMemory<T> cannot have a negative count (-4)",
                "SharedMemory<T> cannot have a negative count (-10)",
                "SharedMemory<T> cannot have a negative count (-2147483648)"
            ],
            diagnostics
                .Where(static d => d.Id == GSharpDiagnostics.SharedMemorySizeNegative.Id)
                .Select(static d => d.Message));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task NegativeConstantCount_DiagnosticSeverity_ReportsError(string dialect)
    {
        var (_, analyzer, _) =
            await KernelDiagnostics.GetDiagnosticsAsync(RoslynTestUtility.ReadTestCase(SadPath), dialect);

        var reported = analyzer
            .Where(static d => d.Id == GSharpDiagnostics.SharedMemorySizeNegative.Id)
            .ToList();

        Assert.Equal(4, reported.Count);
        Assert.All(reported, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }
}
