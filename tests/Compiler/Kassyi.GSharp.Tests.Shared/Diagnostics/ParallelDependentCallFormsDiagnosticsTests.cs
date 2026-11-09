using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// GSH0035 follows a parallel-dependent call whatever the syntax that makes it: a constructor reached through
/// <c>new T()</c> in a generic struct or a target-typed <c>new()</c> (#953), and a getter reached through a bare
/// property name or <c>using static</c>, an indexer, a user-defined unary operator and a user-defined conversion (#1262).
/// </summary>
[Category(TestCategory.Compiler)]
public class ParallelDependentCallFormsDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ParallelDependentCallForms.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_EveryCallForm_ReportsGsh0035AtTheCall(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 84, 20, 84, 30),
                (Analyzer, 85, 20, 85, 34),
                (Analyzer, 86, 22, 86, 27),
                (Analyzer, 88, 20, 88, 29),
                (Analyzer, 89, 20, 89, 26),
                (Analyzer, 90, 20, 90, 26),
                (Analyzer, 91, 27, 91, 31),
                (Analyzer, 92, 29, 92, 32)
            ],
            diagnostics.Ranges(GSharpDiagnostics.ParallelDependentCallFromSingleThreadContext.Id));
    }
}
