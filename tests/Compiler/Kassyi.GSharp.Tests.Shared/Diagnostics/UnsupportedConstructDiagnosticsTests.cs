using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The analyzer reports the constructs that the transpiler cannot emit (GSH0001, GSH0002, GSH0003, GSH0013) once, at
/// the construct; the generator reports none of them (S-46).
/// </summary>
[Category(TestCategory.Compiler)]
public class UnsupportedConstructDiagnosticsTests
{
    [Theory]
    [InlineData("Analyzers/InvalidMappingSadPath.cs", "Cpu", 28, 19, 28, 31)]
    [InlineData("Analyzers/InvalidMappingSadPath.cs", "Cuda", 28, 19, 28, 31)]
    [InlineData("Generator/SadPathDiagnostics.cu.cs", "Cpu", 12, 26, 12, 38)]
    [InlineData("Generator/SadPathDiagnostics.cu.cs", "Cuda", 12, 26, 12, 38)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ClassCreation_ReportsGsh0001Once(string testCase, string dialect, int startLine,
        int startColumn, int endLine, int endColumn)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(testCase, dialect);

        Assert.Equal(
            [(Analyzer, startLine, startColumn, endLine, endColumn)],
            diagnostics.Ranges(GSharpDiagnostics.ObjectCreationNotSupported.Id));
        // The transpiler used to add GSH0003 'ObjectCreationExpression' at the same place
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    [Theory]
    [InlineData("Generator/SadPathDiagnostics.cu.cs", "Cpu", 11, 13, 11, 19)]
    [InlineData("Generator/SadPathDiagnostics.cu.cs", "Cuda", 11, 13, 11, 19)]
    [InlineData("Analyzers/GpuKernel/StringTernary_InGpuKernel.cu.cs", "Cpu", 12, 9, 12, 15)]
    [InlineData("Analyzers/GpuKernel/StringTernary_InGpuKernel.cu.cs", "Cuda", 12, 9, 12, 15)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StringVariable_ReportsGsh0002AtTheType(string testCase, string dialect, int startLine,
        int startColumn, int endLine, int endColumn)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(testCase, dialect);

        Assert.Equal(
            [(Analyzer, startLine, startColumn, endLine, endColumn)],
            diagnostics.Ranges(GSharpDiagnostics.StringNotSupported.Id));
    }

    // Line 53 is 'var text = "abc";'
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ImplicitlyTypedStringVariable_ReportsGsh0002AtVar(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/UnsupportedSyntaxNodes.cu.cs", dialect);

        Assert.Equal([(Analyzer, 53, 9, 53, 12)], diagnostics.Ranges(GSharpDiagnostics.StringNotSupported.Id));
    }

    // default(string) on line 13 and the type argument on line 14; the lambda parameter and the local function's return
    // type are a signature
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_StringTypesInABody_ReportGsh0002OutsideSignatures(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/StringTypeUsages.cu.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 13, 27, 13, 33),
                (Analyzer, 14, 14, 14, 20)
            ],
            diagnostics.Ranges(GSharpDiagnostics.StringNotSupported.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_StackArrayWithInitializer_IsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/SupportedMacroAndArrayForms.cu.cs", dialect);

        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DynamicStackAllocSize_ReportsGsh0003AtTheSize(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/DynamicStackAllocSadPath.cs", dialect);

        Assert.Equal(
            [(Analyzer, 13, 43, 13, 54)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'dynamic array size'"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_SwitchExpressionArms_ReportGsh0003AtTheClauseOrPattern(string dialect)
    {
        var whenAndRelational =
            await KernelDiagnostics.RunTestCaseAsync("Analyzers/GpuKernel/UnsupportedSwitch_InGpuKernel.cu.cs", dialect);
        var propertyAndList = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/UnsupportedSwitchPatterns.cu.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 16, 15, 16, 28),
                (Analyzer, 23, 13, 23, 16)
            ],
            whenAndRelational.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
        Assert.Equal(
            [
                (Analyzer, 21, 13, 21, 27),
                (Analyzer, 32, 13, 32, 23)
            ],
            propertyAndList.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_OperatorMacroWithoutTarget_ReportsGsh0003AtTheAssignment(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/OperatorMacroMissingTarget.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 36, 9, 36, 18)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'OperatorMacroTargets.Missing'"));
    }

    // StructSizeDiagnostics: SharedMemory<double>(5000) is 40000 bytes; 4096 doubles (exactly 32768 bytes) and 10000
    // bytes are not reported. ComprehensiveSpecKernel: SharedMemory<int>(10000); the dynamic size is not checked.
    [Theory]
    [InlineData("Analyzers/StructSizeDiagnostics.cs", "Cpu", 104, 63, 104, 67)]
    [InlineData("Analyzers/StructSizeDiagnostics.cs", "Cuda", 104, 63, 104, 67)]
    [InlineData("ComprehensiveSpecKernel.cu.cs", "Cpu", 107, 63, 107, 68)]
    [InlineData("ComprehensiveSpecKernel.cu.cs", "Cuda", 107, 63, 107, 68)]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_LargeStaticSharedMemory_ReportsGsh0013AtTheSize(string testCase, string dialect, int startLine,
        int startColumn, int endLine, int endColumn)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(testCase, dialect);

        Assert.Equal(
            [(Analyzer, startLine, startColumn, endLine, endColumn)],
            diagnostics.Ranges(GSharpDiagnostics.ExcessiveSharedMemoryWarning.Id));
    }
}
