using Kassyi.GSharp.Compiler.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class GpuKernelDirectInvocationAnalyzerTests : TranspilerTestBase
{
    private static readonly GpuKernelDirectInvocationAnalyzer s_analyzer = new();

    [Fact]
    [Trait("DiagnosticId", "GSH0031")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_GpuKernelDirectInvocation_ReportsGSH0031()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/DirectGpuKernelInvocation.cs", s_analyzer)
                               .ReportsErrorsAsync(GSharpDiagnostics.DirectGpuKernelInvocation);
    }

    [Fact]
    [Trait("DiagnosticId", "GSH0031")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_GpuKernelDirectInvocation_NestedCalls_FollowTheEnclosingFunction()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/DirectGpuKernelInvocationNested.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();

        // Only the lambda in host code is reported; lambdas and local functions inside GPU functions are device code.
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.DirectGpuKernelInvocation.Id, diagnostic.Id);
        var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line;
        Assert.Contains("NestedInvocationKernels.TargetKernel()",
            diagnostic.Location.SourceTree?.GetText().Lines[line].ToString() ?? string.Empty);
    }
}
