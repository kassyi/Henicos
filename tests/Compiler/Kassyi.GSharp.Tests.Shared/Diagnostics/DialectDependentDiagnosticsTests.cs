using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The checks that depend on the dialect (GSH0054, GSH0006, GSH0065, and GSH0003 for a Math member the dialect does not
/// map) come from the analyzer, which reads <c>GSharpTargetDialect</c>; the generator reports none of them (S-46, #461).
/// </summary>
[Category(TestCategory.Compiler)]
public class DialectDependentDiagnosticsTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_InlinePtx_ReportsGsh0054OnlyForTheCpuDialect(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Generator/DialectDiagnostics.cu.cs", dialect);

        IReadOnlyList<(DiagnosticOrigin, int, int, int, int)> expected = dialect == "Cpu"
            ? [(Analyzer, 11, 13, 11, 34), (Analyzer, 18, 13, 18, 34)]
            : [];
        Assert.Equal(expected, diagnostics.Ranges(GSharpDiagnostics.InlinePtxNotSupportedByDialect.Id));
    }

    [Theory]
    [InlineData("Cpu", 1)]
    [InlineData("Cuda", 0)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ComprehensiveSpecKernel_ReportsGsh0054OnlyForTheCpuDialect(string dialect, int count)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("ComprehensiveSpecKernel.cu.cs", dialect);

        Assert.Equal(
            Enumerable.Repeat((Analyzer, 223, 13, 223, 51), count),
            diagnostics.Ranges(GSharpDiagnostics.InlinePtxNotSupportedByDialect.Id));
    }

    // The text must be a constant in every dialect, also where the call becomes a runtime failure
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_InlinePtxVariable_ReportsGsh0006AtTheArgument(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/MacroArgumentSadPath.cs", dialect);

        Assert.Equal(
            [(Analyzer, 21, 23, 21, 29)],
            diagnostics.Ranges(GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral.Id));
    }

    // #461: two references, two diagnostics
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StaticFieldReferences_ReportGsh0065OncePerReference(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/StaticMemberAccessTwice.cu.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 15, 23, 15, 32),
                (Analyzer, 15, 35, 15, 75)
            ],
            diagnostics.Ranges(GSharpDiagnostics.StaticMemberAccessNotSupported.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StaticMembers_ReportGsh0065InKernelsAndDeviceFunctions(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/StaticMemberAccess_SadPath.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 20, 28, 20, 33),
                (Analyzer, 20, 36, 20, 47),
                (Analyzer, 20, 50, 20, 58),
                (Analyzer, 20, 61, 20, 81),
                (Analyzer, 20, 84, 20, 103),
                (Analyzer, 26, 16, 26, 21)
            ],
            diagnostics.Ranges(GSharpDiagnostics.StaticMemberAccessNotSupported.Id));
    }

    // #899: the members of Math and MathF that the dialect maps, and the constants, build with no diagnostic
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_MappedMathMembers_ReportNothing(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/MappedMathMembers.cs", dialect);

        AssertCompiles("Analyzers/MappedMathMembers.cs");
        Assert.Empty(diagnostics);
    }

    // #899: the spec's comprehensive kernel calls Math.Sqrt, Math.Abs, Math.Max and Math.Min
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_ComprehensiveSpecKernel_ReportsNoGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("ComprehensiveSpecKernel.cu.cs", dialect);

        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    // #899: a member that the dialect does not map, also an unmapped overload of a mapped name, is reported with the
    // overload and the dialect, whether it is called with its type, through using static or converted to a delegate
    [Theory]
    [InlineData("Cpu", "StandardCppDialect")]
    [InlineData("Cuda", "GSharpCudaDialect")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UnmappedMathMembers_ReportGsh0003WithTheDialect(string dialect, string dialectName)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/UnmappedMathMembers.cs", dialect);

        AssertCompiles("Analyzers/UnmappedMathMembers.cs");
        Assert.Equal(
            [
                (Analyzer, 17, 16, 17, 24, $"Math.Abs(long) (not mapped by {dialectName})"),
                (Analyzer, 17, 33, 17, 41, $"Math.Max(long, long) (not mapped by {dialectName})"),
                (Analyzer, 18, 16, 18, 26, $"MathF.Cbrt(float) (not mapped by {dialectName})"),
                // A method group: the member access
                (Analyzer, 19, 30, 19, 38, $"Math.Abs(long) (not mapped by {dialectName})"),
                // Through using static System.Math: the whole invocation
                (Analyzer, 20, 16, 20, 26, $"Math.Cbrt(double) (not mapped by {dialectName})"),
                (Analyzer, 21, 16, 21, 25, $"Math.Abs(long) (not mapped by {dialectName})"),
                // A mapped member as a method group, qualified and through using static, and an unmapped one through
                // using static: the member access or the name
                (Analyzer, 22, 29, 22, 37, "Math.Abs(int) (used as a method group)"),
                (Analyzer, 23, 29, 23, 32, "Math.Abs(int) (used as a method group)"),
                (Analyzer, 24, 31, 24, 34, $"Math.Abs(long) (not mapped by {dialectName})")
            ],
            diagnostics
                .Where(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id)
                .Select(static d => (d.Origin, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn, ExtractSyntax(d.Message))));
        Assert.All(diagnostics, static d => Assert.Equal(GSharpDiagnostics.UnsupportedSyntax.Id, d.Id));
    }

    // KernelDiagnostics leaves out the C# errors of the input; a call that does not bind would check nothing
    private static void AssertCompiles(string relativePathToTestCases) =>
        Assert.Empty(RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(relativePathToTestCases))
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error));

    // The '{0}' of "The syntax '{0}' is not supported in GSharp GPU kernels"
    private static string ExtractSyntax(string message)
    {
        var start = message.IndexOf('\'') + 1;
        return message[start..message.LastIndexOf('\'')];
    }
}
