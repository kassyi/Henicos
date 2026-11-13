using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// Verifies GSH0119: a file-local type named in a body that the managed path has to copy into a generated file, where
/// the type is not visible (#427). A body that is not copied may name one; only the dialect that emits managed bodies
/// warns; and the generated code compiles whatever the dialect.
/// </summary>
[Category(TestCategory.Compiler)]
public class FileLocalTypeDiagnosticsTests
{
    private static readonly string s_id = GSharpDiagnostics.FileLocalTypeInManagedCopy.Id;

    private static readonly string s_source =
        RoslynTestUtility.ReadTestCase("Analyzers/FileLocalType_ManagedBody.cu.cs");

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FileLocalTypeInManagedCopy_Descriptor_IsAWarning()
    {
        Assert.Equal(DiagnosticSeverity.Warning, GSharpDiagnostics.FileLocalTypeInManagedCopy.DefaultSeverity);
    }

    // The bodies that need a copy: a kernel that waits at a barrier (names at 109), a suspending function (121), a
    // nested type of a file-local type (129), a generic call of a file-local class (138), an extension Deconstruct
    // (146), a function that holds an operator macro (170), and the kernels that call a function that needs one (the
    // identifier at 152 and 178). The kernels of LocalKernels and the plain device function are not copied, so none
    // of the names in them is reported
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ManagedCopy_FileLocalTypeNamed_WarnsAtEachName()
    {
        var diagnostics = await KernelDiagnostics.RunAsync(s_source, "Cpu");

        Assert.Equal(
            [
                (DiagnosticOrigin.Analyzer, 109, 13, 109, 23),
                (DiagnosticOrigin.Analyzer, 109, 32, 109, 42),
                (DiagnosticOrigin.Analyzer, 121, 13, 121, 23),
                (DiagnosticOrigin.Analyzer, 121, 32, 121, 42),
                (DiagnosticOrigin.Analyzer, 129, 13, 129, 23),
                (DiagnosticOrigin.Analyzer, 138, 24, 138, 33),
                (DiagnosticOrigin.Analyzer, 146, 13, 146, 23),
                (DiagnosticOrigin.Analyzer, 152, 28, 152, 31),
                (DiagnosticOrigin.Analyzer, 170, 13, 170, 23),
                (DiagnosticOrigin.Analyzer, 170, 32, 170, 42),
                (DiagnosticOrigin.Analyzer, 178, 28, 178, 31)
            ],
            diagnostics.Ranges(s_id));
        Assert.DoesNotContain(diagnostics, static d => d.Id == "GSH0048");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ManagedCopy_FileLocalTypeNamed_MessageNamesTypeBodyAndReason()
    {
        var diagnostics = await KernelDiagnostics.RunAsync(s_source, "Cpu");

        var inSuspending = Assert.Single(diagnostics, static d => d.Id == s_id && d is { StartLine: 121, StartColumn: 13 });
        Assert.Equal(
            "'FileLocalRepro.LocalValue' is a file-local type, which generated code cannot name. The managed path of " +
            "the CPU dialect copies the body of 'FileLocalRepro.SuspendingKernels.Wait(int)' into a generated file " +
            "because it suspends at a barrier or a warp exchange, which the managed path runs as a state machine, so a " +
            "kernel that needs that copy cannot run on the managed path: its managed launch throws. The GPU, the " +
            "native CPU path and the C# execution are not affected. Declare the type without 'file', for example as a " +
            "private type nested in the class that uses it.",
            inSuspending.Message);

        // The kernel that calls it names the function whose copy it needs
        var atKernel = Assert.Single(diagnostics, static d => d.Id == s_id && d is { StartLine: 152 });
        Assert.Contains("'FileLocalRepro.LocalValue'", atKernel.Message, StringComparison.Ordinal);
        Assert.Contains("the body of 'FileLocalRepro.SuspendingKernels.Wait(int)'", atKernel.Message,
            StringComparison.Ordinal);

        var rewritten = Assert.Single(diagnostics, static d => d.Id == s_id && d is { StartLine: 170, StartColumn: 13 });
        Assert.Contains("only the managed path rewrites", rewritten.Message, StringComparison.Ordinal);
    }

    // The copy writes what the user's directives resolve as qualified names, so they name the type as well; nameof is
    // written as its string, but a method that is named nameof is an ordinary call
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task ManagedCopy_FileLocalTypeThroughDirectives_WarnsAtUsingStaticAndAliasButNotAtNameof()
    {
        var source = RoslynTestUtility.ReadTestCase("Analyzers/FileLocalType_Directives.cu.cs");
        var diagnostics = await KernelDiagnostics.RunAsync(source, "Cpu");

        Assert.Equal(
            [
                (DiagnosticOrigin.Analyzer, 25, 13, 25, 18),
                (DiagnosticOrigin.Analyzer, 27, 35, 27, 40),
                (DiagnosticOrigin.Analyzer, 41, 42, 41, 47)
            ],
            diagnostics.Ranges(s_id));
        Assert.DoesNotContain(diagnostics, static d => d.Id == "GSH0048");
        var (_, output, _) = GeneratorTestRunner.RunGenerator(source, "Cpu");
        Assert.DoesNotContain(output.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
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

        // On the CPU dialect the managed launch of the kernels that need a copy throws instead of naming the type
        Assert.DoesNotContain(output.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    }

    // How the managed launch of each kernel runs: the user's method where no copy is needed, nothing where a body that
    // needs a copy names a file-local type, the copy as always where no file-local type is named
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("FileLocalRepro.LocalKernels", "Run", nameof(ManagedKernelMode.UserMethod))]
    [InlineData("FileLocalRepro.LocalKernels", "CallStatic", nameof(ManagedKernelMode.UserMethod))]
    [InlineData("FileLocalRepro.LocalKernels", "CallExtension", nameof(ManagedKernelMode.UserMethod))]
    [InlineData("FileLocalRepro.LocalKernels", "CallGeneric", nameof(ManagedKernelMode.UserMethod))]
    [InlineData("FileLocalRepro.LocalKernels", "Deconstruct", nameof(ManagedKernelMode.UserMethod))]
    [InlineData("FileLocalRepro.LocalKernels", "Nested", nameof(ManagedKernelMode.UserMethod))]
    [InlineData("FileLocalRepro.PlainKernels", "Run", nameof(ManagedKernelMode.Copy))]
    [InlineData("FileLocalRepro.BarrierKernels", "Sync", nameof(ManagedKernelMode.Unavailable))]
    [InlineData("FileLocalRepro.SuspendingKernels", "Run", nameof(ManagedKernelMode.Unavailable))]
    [InlineData("FileLocalRepro.MacroFunctions", "Run", nameof(ManagedKernelMode.Unavailable))]
    public void ManagedMode_KernelsAndTheirCopies_FollowsTheNeedForACopy(string className, string kernel,
        string expected)
    {
        var host = CreateHost(className, kernel);

        Assert.Equal(expected, host.ManagedMode.ToString());
        Assert.Equal(expected == nameof(ManagedKernelMode.Unavailable), host.ManagedUnavailableReason is not null);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("FileLocalRepro.BarrierKernels", "Sync", "FileLocalRepro.BarrierKernels.Sync(int[])", "reaches")]
    [InlineData("FileLocalRepro.SuspendingKernels", "Run", "FileLocalRepro.SuspendingKernels.Wait(int)", "suspends")]
    [InlineData("FileLocalRepro.MacroFunctions", "Run", "FileLocalRepro.MacroFunctions.Rewritten(",
        "only the managed path rewrites")]
    public void ManagedUnavailableReason_NeedsACopy_NamesTheBodyTheTypeAndWhy(string className, string kernel,
        string body, string why)
    {
        var reason = CreateHost(className, kernel).ManagedUnavailableReason;

        Assert.NotNull(reason);
        Assert.StartsWith($"{kernel} cannot run on the managed path of the CPU dialect: it needs a copy of '",
            reason, StringComparison.Ordinal);
        Assert.Contains(body, reason, StringComparison.Ordinal);
        Assert.Contains(why, reason, StringComparison.Ordinal);
        Assert.EndsWith(
            "names the file-local type 'FileLocalRepro.LocalValue' (" + s_id + ").",
            reason,
            StringComparison.Ordinal);
    }

    private static KernelHostModel CreateHost(string className, string kernel)
    {
        var compilation = RoslynTestUtility.CreateCompilation(s_source);
        var method = compilation.GetTypeByMetadataName(className)?.GetMembers(kernel).OfType<IMethodSymbol>().Single()
                     ?? throw new InvalidOperationException(className);
        var model = compilation.GetSemanticModel(method.DeclaringSyntaxReferences[0].SyntaxTree);

        return KernelHostModel.Create(method, default, model);
    }
}
