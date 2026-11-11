using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A <c>ref</c> local in a method that the CPU dialects split into states: one that is live after a split is GSH0039 at
/// its declaration, and the generated C# compiles so that a build shows GSH0039 (a class cannot hold the ref field,
/// CS9059); one used only before the split stays a local of its state (#1640).
/// </summary>
[Category(TestCategory.Compiler)]
public class RefLocalAcrossSplitDiagnosticsTests
{
    private const string TestCase = "Diagnostics/RefLocalAcrossSplit.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task RefLocal_LiveAcrossSplit_ReportsGsh0039AtTheDeclaration(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // Across Gpu.SyncThreads(), a suspending call, a warp shuffle and a barrier in a device function; the locals
        // used only before the split and the rebound one are not reported
        Assert.Equal(
            [
                (Analyzer, 22, 17, 22, 22),
                (Analyzer, 33, 17, 33, 22),
                (Analyzer, 44, 17, 44, 21),
                (Analyzer, 55, 17, 55, 22),
            ],
            diagnostics.Ranges(GSharpDiagnostics.CrossBarrierRefStructProhibited.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task RefLocal_InSplitMethods_GeneratedCSharpCompiles(string dialect)
    {
        var run = await RunAsync(dialect);

        // A build shows the analyzer's diagnostics only when the C# compiles: an error in generated code (CS9059 for a
        // ref field of the frame class, CS0103 for a local of another state) would hide GSH0039
        // mutant: ManagedContextFields.ProcessContextFields without the ref local check -> CS9059 for every held local
        // mutant: ManagedCSharpTranspiler.Generate always rewriting the body -> CS1061 for the missing field
        var errors = run.Output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();
        Assert.Empty(errors);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task RefLocal_HeldAcrossSplit_StateMachineThrowsWithoutAFrameField()
    {
        var generated = GeneratedSource(await RunAsync("Cpu"));

        Assert.Contains(
            "throw new global::System.NotSupportedException(\"HeldAcrossSyncThreads cannot run on the CPU: the ref " +
            "local 'alias' cannot be kept across a barrier or a call that may suspend (GSH0039).\");",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "throw new global::System.NotSupportedException(\"HeldInDeviceFunction cannot run on the CPU: the ref " +
            "local 'alias' cannot be kept across a barrier or a call that may suspend (GSH0039).\");",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain("public ref int", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("internal ref int", generated, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task RefLocal_UsedOnlyBeforeTheSplit_StaysALocalOfItsState()
    {
        var generated = GeneratedSource(await RunAsync("Cpu"));

        // mutant: HoistedVariableAnalysis.MustHoist `!staysLocal &&` removed -> the local becomes a field (CS9059)
        Assert.Contains("ref int alias = ref __gsharp_ctx.w;", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("UsedBeforeSyncThreads cannot run", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UsedBeforeSyncThreadsInDeviceFunction cannot run",
            generated,
            StringComparison.Ordinal);
    }

    private static async Task<KernelDiagnosticsRun> RunAsync(string dialect)
    {
        var path = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "TestCases", TestCase);
        var compilation = RoslynTestUtility.CreateCompilationAt(path, RoslynTestUtility.ReadTestCase(TestCase));
        return await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
    }

    private static string GeneratedSource(KernelDiagnosticsRun run) =>
        string.Join(
            "\n",
            run.Output.SyntaxTrees
                .Where(tree => !run.Input.SyntaxTrees.Contains(tree))
                .Select(static tree => tree.ToString()));
}
