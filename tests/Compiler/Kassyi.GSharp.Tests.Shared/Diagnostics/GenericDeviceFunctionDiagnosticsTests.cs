#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// G# does not compile a generic device function as it is written: the analyzer reports GSH0123 once at its name, and
/// the generator neither transpiles nor lowers it, so a function that reaches a barrier no longer fails the generator
/// (GSH0048) or leaves generated C# that names a frame it did not write.
/// </summary>
[Category(TestCategory.Compiler)]
public class GenericDeviceFunctionDiagnosticsTests
{
    private const string TestCase = "Diagnostics/GenericDeviceFunction.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Pick_GenericDeviceFunction_ReportsGsh0123AtTheName(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 6, 21, 6, 25)],
            diagnostics.Ranges(GSharpDiagnostics.GenericDeviceFunctionNotCompiled.Id, "'Pick'"));
        Assert.Empty(diagnostics.Where(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task AfterSync_GenericDeviceFunctionWithBarrier_ReportsGsh0123AtTheName(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 9, 21, 9, 30)],
            diagnostics.Ranges(GSharpDiagnostics.GenericDeviceFunctionNotCompiled.Id, "'AfterSync'"));
        Assert.Equal(
            "'AfterSync' cannot be compiled for the GPU: generic device functions are not supported yet.",
            diagnostics.Single(static d => d.Id == GSharpDiagnostics.GenericDeviceFunctionNotCompiled.Id &&
                                           d.StartLine == 9).Message);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Twice_MemberOfGenericTypeWithoutOwnTypeParameters_ReportsNothing(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.DoesNotContain(diagnostics, static d => d.StartLine is >= 24 and < 31);
        Assert.Equal(2, diagnostics.Count(static d => d.Id == GSharpDiagnostics.GenericDeviceFunctionNotCompiled.Id));
    }

    // A generic device function whose type parameters appear in neither its signature nor its body (as the fuzzer's
    // device functions are) is compiled as before, also one that waits at a barrier, and the kernel's state machine
    // with it
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Phantom_TypeParametersNamedNowhere_ReportsNothing(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.DoesNotContain(run.Analyzer, static d => d.Location.GetLineSpan().StartLinePosition.Line >= 31);
        // Only the two functions that name their type parameter are stated as #error
        var id = GSharpDiagnostics.GenericDeviceFunctionNotCompiled.Id;
        var stated = KernelDiagnostics.FindStatedFailures(run);
        Assert.Equal(2, stated.Count);
        Assert.Contains(stated, f => f.Failure.EndsWith($"(6,21): {id}", StringComparison.Ordinal));
        Assert.Contains(stated, f => f.Failure.EndsWith($"(9,21): {id}", StringComparison.Ordinal));
        Assert.DoesNotContain(
            run.Output.GetDiagnostics(),
            static d => d.Severity == DiagnosticSeverity.Error &&
                        d.GetMessage().Contains("Phantom", StringComparison.Ordinal));
    }

    // The generator states GSH0123 as #error where the analyzer reports it, and the generated C# compiles as far as
    // the frames and state machines go: before, the lowering of the open declaration threw (GSH0048) and the kernel's
    // managed state machine named a frame that was never written (CS0426, CS0117, CS0246)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task AfterSync_GenericDeviceFunctionWithBarrier_GeneratorReportsNoInternalError(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.DoesNotContain(run.Generator, static d => d.Id == GSharpDiagnostics.InternalCompilerError.Id);
        Assert.Empty(run.Output.GetDiagnostics().Where(static d => d.Id is "CS0426" or "CS0117" or "CS0246"));
        var stated = KernelDiagnostics.FindStatedFailures(run);
        Assert.Contains(stated, static f => f.Failure.EndsWith(
            $"(9,21): {GSharpDiagnostics.GenericDeviceFunctionNotCompiled.Id}",
            StringComparison.Ordinal));
        Assert.All(stated, static f => Assert.True(f.IsReported, f.Failure));
    }

    private const string ForwarderCase = "Diagnostics/GenericForwarderChain.cu.cs";

    // Far above a linear analysis of the case (well under a second); a walk that re-reads each callee wherever it meets
    // it needs about three times as long per forwarder, 43 s for a chain of 12, and never finishes a chain of 30
    private static readonly TimeSpan s_forwarderBudget = TimeSpan.FromSeconds(60);

    private static string LeftOutMessage(string name) =>
        $"'{name}' cannot be compiled for the GPU: generic device functions are not supported yet.";

    private static async Task<IReadOnlyList<string>> ReportedForwardersAsync(string dialect)
    {
        var diagnostics = await Task.Run(() => KernelDiagnostics.RunTestCaseAsync(ForwarderCase, dialect))
            .WaitAsync(s_forwarderBudget);
        return diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.GenericDeviceFunctionNotCompiled.Id)
            .Select(static d => d.Message)
            .ToList();
    }

    // Thirty generic functions that only pass T on (Fi<T> calls Fi+1<T>), and twenty that call the next one twice:
    // none uses its type parameter, so each is compiled once, and the analysis answers in time
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Forwarders_ChainAndDiamondOfPhantomForwarders_ReportNothingInTime(string dialect)
    {
        var reported = await ReportedForwardersAsync(dialect);

        Assert.DoesNotContain(reported, static m => m.StartsWith("'F", StringComparison.Ordinal));
        Assert.DoesNotContain(reported, static m => m.StartsWith("'D", StringComparison.Ordinal));
    }

    // A chain whose last function uses T leaves out every function that passes T on to it, each reported once at its
    // name; in a cycle, Ping and Pong only pass T to each other and are compiled once, while A is left out with B,
    // which uses T
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Forwarders_PassingOnToAFunctionThatUsesT_ReportsEachForwarder(string dialect)
    {
        var reported = await ReportedForwardersAsync(dialect);

        string[] expected =
        [
            .. Enumerable.Range(0, 10).Select(static i => LeftOutMessage($"U{i}")),
            LeftOutMessage("A"),
            LeftOutMessage("B"),
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), reported.Order(StringComparer.Ordinal));
    }

    // Every function the generator leaves out is stated as #error where the analyzer reports GSH0123
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Forwarders_LeftOutForwarders_GeneratorStatesThemWhereTheAnalyzerReports(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(ForwarderCase));

        var run = await Task.Run(() => KernelDiagnostics.RunCompilationAsync(compilation, dialect))
            .WaitAsync(s_forwarderBudget);

        var stated = KernelDiagnostics.FindStatedFailures(run)
            .Where(static f => f.Failure.EndsWith(GSharpDiagnostics.GenericDeviceFunctionNotCompiled.Id,
                StringComparison.Ordinal))
            .ToList();
        Assert.Equal(12, stated.Count);
        Assert.All(stated, static f => Assert.True(f.IsReported, f.Failure));
    }
}
