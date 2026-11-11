using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A <c>ref</c> local in a method that the CPU dialects split into states: one that is live after a split is GSH0039 at
/// its declaration; one that the state machine would keep in its frame for another reason (rebound, used in another
/// switch section, a ref foreach variable used across a barrier) is GSH0003 from the generator on the CPU dialect; the
/// generated C# compiles so that a build shows them (a class cannot hold the ref field, CS9059); one used only before
/// the split stays a local of its state (#1640).
/// </summary>
[Category(TestCategory.Compiler)]
public class RefLocalAcrossSplitDiagnosticsTests
{
    private const string TestCase = "Diagnostics/RefLocalAcrossSplit.cu.cs";
    private const string HeldTestCase = "Diagnostics/RefLocalHeldByCpuStateMachine.cu.cs";

    private static string Gsh0003 => GSharpDiagnostics.UnsupportedSyntax.Id;

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

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task RefLocal_HeldWithoutGsh0039_ReportsGsh0003FromTheGeneratorOnTheCpuDialect()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");
        var held = await KernelDiagnostics.RunTestCaseAsync(HeldTestCase, "Cpu");

        // Rebound after the barrier, rebound before it, used in a switch section after the one with the barrier, a ref
        // foreach variable read after the barrier of its iteration (in a kernel and in a device function) and one used
        // only before it; not the locals that GSH0039 reports
        // mutant: AstLoweringPipeline.FindHeldRefLocals `isReportedLive` always true -> nothing is reported
        // mutant: GpuKernelGenerator.ReportHeldRefLocals `if (config.IsCuda)` -> `if (!config.IsCuda)` -> nothing on
        // the CPU dialect
        const string Constraint = GpuCodeConstraints.RefLocalHeldByCpuStateMachine;
        Assert.Equal([(DiagnosticOrigin.Generator, 131, 17, 131, 22)], diagnostics.Ranges(Gsh0003, Constraint));
        Assert.Equal(
            [
                (DiagnosticOrigin.Generator, 19, 17, 19, 22),
                (DiagnosticOrigin.Generator, 32, 17, 32, 22),
                (DiagnosticOrigin.Generator, 51, 26, 51, 27),
                (DiagnosticOrigin.Generator, 62, 26, 62, 27),
                (DiagnosticOrigin.Generator, 73, 26, 73, 27),
            ],
            held.Ranges(Gsh0003, Constraint));
        Assert.Empty(held.Ranges(GSharpDiagnostics.CrossBarrierRefStructProhibited.Id));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task RefLocal_HeldWithoutGsh0039_IsNotReportedOnTheCudaDialect()
    {
        var held = await KernelDiagnostics.RunTestCaseAsync(HeldTestCase, "Cuda");

        // The CUDA dialect keeps every thread on the GPU and splits nothing (#929)
        Assert.Empty(held.Ranges(Gsh0003, GpuCodeConstraints.RefLocalHeldByCpuStateMachine));
    }

    [Theory]
    [InlineData(TestCase, "Cpu")]
    [InlineData(TestCase, "Cuda")]
    [InlineData(HeldTestCase, "Cpu")]
    [InlineData(HeldTestCase, "Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task RefLocal_InSplitMethods_GeneratedCSharpCompiles(string testCase, string dialect)
    {
        var run = await RunAsync(testCase, dialect);

        // A build shows the analyzer's diagnostics only when the C# compiles: an error in generated code (CS9059 for a
        // ref field of the frame class, CS0103 for a local of another state) would hide GSH0039
        // mutant: ManagedContextFields.ProcessContextFields without the ref local check -> CS9059 for every held local
        // mutant: ManagedCSharpTranspiler.CreateBody always rewriting the body -> CS1061 for the missing field
        var errors = run.Output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(TestCase)]
    [InlineData(HeldTestCase)]
    [Scenario(TestScenario.SadPath)]
    public async Task RefLocal_ThrowingStateMachine_ComesWithAnErrorAtEachHeldLocal(string testCase)
    {
        var run = await RunAsync(testCase, "Cpu");
        var generated = GeneratedSource(run);

        // No build may succeed with a state machine that only throws: each one belongs to a method whose held ref
        // locals are GSH0039 (analyzer) or GSH0003 (generator)
        var throwingMethods = Regex.Matches(
                generated,
                "NotSupportedException\\(\"(?<method>\\w+) cannot run on the CPU: the ref local '(?<local>\\w+)'")
            .Select(static m => m.Groups["method"].Value)
            .ToList();
        Assert.NotEmpty(throwingMethods);
        var errorLines = run.Analyzer.Concat(run.Generator)
            .Where(static d => d.Severity == DiagnosticSeverity.Error &&
                               (d.Id == GSharpDiagnostics.CrossBarrierRefStructProhibited.Id ||
                                d.GetMessage().Contains(GpuCodeConstraints.RefLocalHeldByCpuStateMachine,
                                    StringComparison.Ordinal)))
            .Select(static d => d.Location.GetLineSpan().StartLinePosition.Line)
            .ToHashSet();
        var methods = run.Input.SyntaxTrees.Last()
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToList();
        Assert.All(
            throwingMethods,
            name =>
            {
                var method = methods.Single(m => m.Identifier.ValueText == name);
                var lines = method.SyntaxTree.GetLineSpan(method.Span);
                Assert.Contains(
                    errorLines,
                    line => line >= lines.StartLinePosition.Line && line <= lines.EndLinePosition.Line);
            });
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task RefLocal_HeldAcrossSplit_StateMachineThrowsWithoutAFrameField()
    {
        var generated = GeneratedSource(await RunAsync(TestCase, "Cpu"));

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
        var generated = GeneratedSource(await RunAsync(TestCase, "Cpu"));

        // mutant: HoistedVariableAnalysis.MustHoist `!staysLocal &&` removed -> the local becomes a field (CS9059)
        Assert.Contains("ref int alias = ref __gsharp_ctx.w;", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("UsedBeforeSyncThreads cannot run", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UsedBeforeSyncThreadsInDeviceFunction cannot run",
            generated,
            StringComparison.Ordinal);
    }

    private static async Task<KernelDiagnosticsRun> RunAsync(string testCase, string dialect)
    {
        var path = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "TestCases", testCase);
        var compilation = RoslynTestUtility.CreateCompilationAt(path, RoslynTestUtility.ReadTestCase(testCase));
        return await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
    }

    private static string GeneratedSource(KernelDiagnosticsRun run) =>
        string.Join(
            "\n",
            run.Output.SyntaxTrees
                .Where(tree => !run.Input.SyntaxTrees.Contains(tree))
                .Select(static tree => tree.ToString()));
}
