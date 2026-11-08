using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A <c>with</c> on a struct is emitted (StructWithExpressionTests). The analyzer reports, once and at the construct
/// (GSH0003), the <c>with</c> that the device cannot emit: on a class, which allocates, and on a [GpuReadOnly] member.
/// </summary>
[Category(TestCategory.Compiler)]
public class WithExpressionDiagnosticsTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_WithOnAClass_ReportsGsh0003AtTheExpression(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/WithExpressionUnsupported.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 44, 20, 44, 41)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "with expression on a reference type"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_WithAssigningAPropertyWithABody_IsSupported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/WithExpressionUnsupported.cu.cs", dialect);

        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "Value"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_WithAssigningAGpuReadOnlyMember_ReportsGsh0003AtTheMember(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/WithExpressionUnsupported.cu.cs", dialect);

        // 'Free' on the same line is not reported
        Assert.Equal(
            [(Analyzer, 62, 44, 62, 49)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "the [GpuReadOnly] member 'Fixed'"));
    }

    // Compound assignment, increment and chained assignment of a computed property are written as getter and setter
    // calls (#908)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_ComputedPropertyCompoundIncrementAndChainedAssignment_AreSupported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(
            "Diagnostics/WithExpressionUnsupported.cu.cs", dialect);

        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "computed property"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UnsupportedWith_IsTheOnlyGsh0003Reported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/WithExpressionUnsupported.cu.cs", dialect);

        Assert.Equal(2, diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id).Count);
    }
}
