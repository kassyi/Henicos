using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A <c>with</c> on a struct is emitted (StructWithExpressionTests). The analyzer reports, once and at the construct
/// (GSH0003), the <c>with</c> that the device cannot emit: on a class, which allocates, and on a member that the device
/// struct cannot assign (a property whose init accessor has a body, a [GpuReadOnly] member).
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
            [(Analyzer, 33, 20, 33, 41)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "with expression on a reference type"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_WithAssigningAPropertyWithABody_ReportsGsh0003AtTheMember(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/WithExpressionUnsupported.cu.cs", dialect);

        // 'Other' (an auto-property) on the same line is not reported
        Assert.Equal(
            [(Analyzer, 42, 45, 42, 50)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "the property 'Value', whose accessor has a body"));
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
            [(Analyzer, 51, 44, 51, 49)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "the [GpuReadOnly] member 'Fixed'"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UnsupportedWith_IsTheOnlyGsh0003Reported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/WithExpressionUnsupported.cu.cs", dialect);

        Assert.Equal(3, diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id).Count);
    }
}
