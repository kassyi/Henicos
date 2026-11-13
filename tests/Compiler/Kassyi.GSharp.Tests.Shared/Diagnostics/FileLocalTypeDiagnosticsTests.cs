using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// Verifies GSH0115: a file-local type named in a body that the managed path copies into a generated file, where the
/// type is not visible (#427). Only the dialect that emits managed bodies reports it, and the generated code compiles.
/// </summary>
[Category(TestCategory.Compiler)]
public class FileLocalTypeDiagnosticsTests
{
    private static readonly string s_id = GSharpDiagnostics.FileLocalTypeInManagedCopy.Id;

    private static readonly string s_source =
        RoslynTestUtility.ReadTestCase("Analyzers/FileLocalType_ManagedBody.cu.cs");

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ManagedBody_FileLocalTypeNamed_ReportsGSH0115AtEachName()
    {
        var diagnostics = await KernelDiagnostics.RunAsync(s_source, "Cpu");

        Assert.Equal(
            [
                // LocalKernels.Run (#427)
                (DiagnosticOrigin.Analyzer, 25, 13, 25, 23),
                (DiagnosticOrigin.Analyzer, 25, 32, 25, 42),
                // LocalKernels.CallStatic: the class of a static call
                (DiagnosticOrigin.Analyzer, 32, 35, 32, 44),
                // LocalKernels.CallExtension: the extension method, whose class the reduced call does not spell
                (DiagnosticOrigin.Analyzer, 38, 55, 38, 61),
                // SuspendingKernels.Wait: the body of its state machine
                (DiagnosticOrigin.Analyzer, 48, 13, 48, 23),
                (DiagnosticOrigin.Analyzer, 48, 32, 48, 42)
            ],
            diagnostics.Ranges(s_id));
        Assert.DoesNotContain(diagnostics, static d => d.Id == "GSH0048");
        var first = Assert.Single(diagnostics, static d => d.Id == s_id && d is { StartLine: 25, StartColumn: 13 });
        Assert.Equal(
            "'FileLocalRepro.LocalValue' is a file-local type; the managed path copies the body of " +
            "'FileLocalRepro.LocalKernels.Run(int[])' into a generated file, where it is not visible. Declare the type " +
            "without 'file', for example as a private type nested in the class that uses it.",
            first.Message);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task CudaBody_FileLocalTypeNamed_ReportsNothing()
    {
        // The CUDA dialect transpiles the body to C++ and copies no C#
        var diagnostics = await KernelDiagnostics.RunAsync(s_source, "Cuda");

        Assert.Empty(diagnostics.Ranges(s_id));
        Assert.DoesNotContain(diagnostics, static d => d.Id == "GSH0048");
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void Generator_FileLocalTypeNamed_GeneratesCompilableCode(string dialect)
    {
        var (_, output, _) = GeneratorTestRunner.RunGenerator(s_source, dialect);

        // On the CPU dialect the launchers of the reported kernels throw instead of copying the body
        Assert.DoesNotContain(output.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    }
    // The CPU launcher throws for the kernels whose managed fallback needs a copy that names a file-local type: the
    // kernel's own body, or a suspending callee's state machine; a device function called as written needs no copy
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("FileLocalRepro.LocalKernels", "Run", "FileLocalRepro.LocalKernels.Run(int[])")]
    [InlineData("FileLocalRepro.LocalKernels", "CallExtension", "FileLocalRepro.LocalKernels.CallExtension(int[])")]
    [InlineData("FileLocalRepro.SuspendingKernels", "Run", "FileLocalRepro.SuspendingKernels.Wait(int)")]
    [InlineData("FileLocalRepro.PlainKernels", "Run", null)]
    public void CpuLauncherUnavailableReason_KernelsAndTheirCopies_NamesTheBlockedBody(string className, string kernel,
        string? blocked)
    {
        var compilation = RoslynTestUtility.CreateCompilation(s_source);
        var method = compilation.GetTypeByMetadataName(className)?.GetMembers(kernel).OfType<IMethodSymbol>().Single()
                     ?? throw new InvalidOperationException(className);
        var model = compilation.GetSemanticModel(method.DeclaringSyntaxReferences[0].SyntaxTree);

        var host = KernelHostModel.Create(method, default, model);

        Assert.Null(host.LauncherUnavailableReason);
        Assert.Equal(
            blocked is null
                ? null
                : $"{kernel} cannot be launched on the CPU dialect: the managed path cannot copy the body of " +
                  $"'{blocked}', which names a file-local type (GSH0115).",
            host.CpuLauncherUnavailableReason);
    }
    // The copy writes what the user's directives resolve as qualified names, so they name the file-local type as well
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task ManagedBody_FileLocalTypeThroughDirectives_ReportsUsingStaticAndAliasButNotNameof()
    {
        var source = RoslynTestUtility.ReadTestCase("Analyzers/FileLocalType_Directives.cu.cs");
        var diagnostics = await KernelDiagnostics.RunAsync(source, "Cpu");

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 25, 13, 25, 18), (DiagnosticOrigin.Analyzer, 26, 35, 26, 40)],
            diagnostics.Ranges(s_id));
        Assert.DoesNotContain(diagnostics, static d => d.Id == "GSH0048");
        var (_, output, _) = GeneratorTestRunner.RunGenerator(source, "Cpu");
        Assert.DoesNotContain(output.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    }
}
