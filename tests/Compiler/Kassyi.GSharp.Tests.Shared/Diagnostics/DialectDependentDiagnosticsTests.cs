using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The checks that depend on the dialect (GSH0054, GSH0006, GSH0065, and GSH0109 for a Math member the dialect does
/// not map) come from the analyzer, which reads <c>GSharpTargetDialect</c>; the generator reports none of them (S-46,
/// #461).
/// </summary>
[Category(TestCategory.Compiler)]
public class DialectDependentDiagnosticsTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task InlinePtx_CpuDialect_ReportsGsh0054(string dialect)
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
    public async Task ComprehensiveSpecKernel_CpuDialect_ReportsGsh0054(string dialect, int count)
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
    public async Task InlinePtxVariable_VariablePtxString_ReportsGsh0006AtArgument(string dialect)
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
    public async Task StaticFieldReferences_DeviceCodeUsage_ReportsGsh0065OncePerReference(string dialect)
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
    public async Task StaticMembers_KernelsAndDeviceFunctions_ReportsGsh0065(string dialect)
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
    public async Task MappedMathMembers_SupportedDialect_ReportsNothing(string dialect)
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
    public async Task ComprehensiveSpecKernel_ValidDialect_ReportsNoGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("ComprehensiveSpecKernel.cu.cs", dialect);

        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    // #899: a member that the dialect does not map, also an unmapped overload of a mapped name, is reported as GSH0109
    // with the overload and the dialect, whether it is called with its type, through using static or converted to a
    // delegate; a mapped member converted to a delegate is GSH0003
    [Theory]
    [InlineData("Cpu", "StandardCppDialect")]
    [InlineData("Cuda", "GSharpCudaDialect")]
    [Scenario(TestScenario.SadPath)]
    public async Task UnmappedMathMembers_UnsupportedDialect_ReportsGsh0109WithDialectName(
        string dialect,
        string dialectName)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/UnmappedMathMembers.cs", dialect);

        AssertCompiles("Analyzers/UnmappedMathMembers.cs");
        const string Unmapped = "GSH0109";
        const string MethodGroup = "GSH0003";
        Assert.Equal(
            [
                // A call: the whole invocation
                (Analyzer, 17, 16, 17, 30, Unmapped, "'Math.Abs(long)'"),
                (Analyzer, 17, 33, 17, 51, Unmapped, "'Math.Max(long, long)'"),
                (Analyzer, 18, 16, 18, 32, Unmapped, "'MathF.Cbrt(float)'"),
                // A method group: the member access
                (Analyzer, 19, 30, 19, 38, Unmapped, "'Math.Abs(long)'"),
                // Through using static System.Math
                (Analyzer, 20, 16, 20, 26, Unmapped, "'Math.Cbrt(double)'"),
                (Analyzer, 21, 16, 21, 25, Unmapped, "'Math.Abs(long)'"),
                // A mapped member as a method group, qualified and through using static, and an unmapped one through
                // using static: the member access or the name
                (Analyzer, 22, 29, 22, 37, MethodGroup, "Math.Abs(int) (used as a method group)"),
                (Analyzer, 23, 29, 23, 32, MethodGroup, "Math.Abs(int) (used as a method group)"),
                (Analyzer, 24, 31, 24, 34, Unmapped, "'Math.Abs(long)'")
            ],
            diagnostics.Select(static d => (d.Origin, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn, d.Id,
                d.Id == Unmapped ? ExtractMethod(d.Message) : ExtractSyntax(d.Message))));
        Assert.All(
            diagnostics.Where(static d => d.Id == Unmapped),
            d => Assert.Contains($"has no implementation in the '{dialectName}' dialect", d.Message));
    }

    // KernelDiagnostics leaves out the C# errors of the input; a call that does not bind would check nothing
    private static void AssertCompiles(string relativePathToTestCases) =>
        Assert.Empty(RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(relativePathToTestCases))
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error));

    // The quoted '{0}' of "The method '{0}' has no implementation in the '{1}' dialect ...", quotes included
    private static string ExtractMethod(string message)
    {
        var start = message.IndexOf('\'');
        return message[start..(message.IndexOf('\'', start + 1) + 1)];
    }

    // The '{0}' of "The syntax '{0}' is not supported in GSharp GPU kernels"
    private static string ExtractSyntax(string message)
    {
        var start = message.IndexOf('\'') + 1;
        return message[start..message.LastIndexOf('\'')];
    }
}
