#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// G# does not compile a generic device function as it is written: the analyzer reports GSH0121 once at its name, and
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
    public async Task Pick_GenericDeviceFunction_ReportsGsh0121AtTheName(string dialect)
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
    public async Task AfterSync_GenericDeviceFunctionWithBarrier_ReportsGsh0121AtTheName(string dialect)
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

        Assert.DoesNotContain(diagnostics, static d => d.StartLine >= 24);
        Assert.Equal(2, diagnostics.Count(static d => d.Id == GSharpDiagnostics.GenericDeviceFunctionNotCompiled.Id));
    }

    // The generator states GSH0121 as #error where the analyzer reports it, and the generated C# compiles as far as
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
}
