using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A struct member body that device code runs (constructor, member initializer, accessor, conversion operator, the
/// truth operators that a <c>&amp;&amp;</c> over an operator macro calls, a <c>Dispose</c> that a device <c>using</c>
/// calls) is not turned into a state machine, so a call there of a device function that reaches a barrier is GSH0062
/// at the call, with or without <c>[GpuDeviceFunction]</c> on the member, and once however many GPU methods use the
/// struct. Members that only the host calls, a struct that no device code uses, a class and a static device function
/// of a struct are not reported.
/// </summary>
[Category(TestCategory.Compiler)]
public class SuspendingCallInStructMemberDiagnosticsTests
{
    private const string TestCase = "Diagnostics/SuspendingCallInStructMember.cs";

    private const string Callee =
        "is a device function that reaches Gpu.SyncThreads() or a warp shuffle, so it cannot be called ";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task StructMemberBody_SuspendingCall_ReportsGsh0062OnceAtEachCall(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 112, 21, 112, 38),
                (Analyzer, 117, 42, 117, 57),
                (Analyzer, 121, 31, 121, 50),
                (Analyzer, 127, 25, 127, 44),
                (Analyzer, 144, 26, 144, 41),
                (Analyzer, 146, 42, 146, 57),
                (Analyzer, 155, 68, 155, 89),
                (Analyzer, 160, 24, 160, 39),
                (Analyzer, 165, 24, 165, 39),
                (Analyzer, 175, 20, 175, 39),
                (Analyzer, 181, 28, 181, 47),
                (Analyzer, 185, 68, 185, 89),
                (Analyzer, 198, 17, 198, 32),
                (Analyzer, 212, 60, 212, 81),
                (Analyzer, 214, 61, 214, 82)
            ],
            Sorted(diagnostics.Ranges(GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id)));
        Assert.All(
            diagnostics,
            static d => Assert.Equal(GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, d.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task StructMemberBody_KindOfMember_NamesTheMemberInTheMessage(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [112, 117, 144, 146, 160, 165],
            StartLines(diagnostics, "in a constructor"));
        Assert.Equal([121, 127, 175, 181], StartLines(diagnostics, "in a property accessor"));
        Assert.Equal([155, 185], StartLines(diagnostics, "in a conversion operator"));
        Assert.Equal([212, 214], StartLines(diagnostics, "in a user-defined operator"));
        Assert.Equal([198], StartLines(diagnostics, "in a method of a struct"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task StructMemberBody_GeneratedSource_StatesEachFailureWhereAnalyzerReportsIt(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        var stated = KernelDiagnostics.FindStatedFailures(run);
        Assert.Equal(15, stated.Count);
        Assert.All(stated, static f => Assert.True(f.IsReported, f.Failure));
        Assert.All(stated, static f => Assert.EndsWith("GSH0062", f.Failure, StringComparison.Ordinal));
    }

    private static List<(DiagnosticOrigin, int, int, int, int)> Sorted(
        IReadOnlyList<(DiagnosticOrigin Origin, int StartLine, int StartColumn, int EndLine, int EndColumn)> ranges) =>
        ranges
            .OrderBy(static r => r.StartLine)
            .ThenBy(static r => r.StartColumn)
            .Select(static r => (r.Origin, r.StartLine, r.StartColumn, r.EndLine, r.EndColumn))
            .ToList();

    private static int[] StartLines(ImmutableArray<ReportedDiagnostic> diagnostics, string member) =>
        diagnostics
            .Ranges(GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, Callee + member)
            .Select(static r => r.StartLine)
            .Order()
            .ToArray();
}
