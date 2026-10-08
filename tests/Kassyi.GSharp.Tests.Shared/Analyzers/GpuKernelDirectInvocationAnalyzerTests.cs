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
                               .ReportsErrorsAsync("GSH0031");
    }
}
