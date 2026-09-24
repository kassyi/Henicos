using Kassyi.GSharp.Compiler.Diagnostics;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GSH_31_GpuKernelDirectInvocationAnalyzerTests : TranspilerTestBase
{
    private static readonly GpuKernelDirectInvocationAnalyzer s_analyzer = new();

    [Fact]
    [Trait("DiagnosticId", "GSH0031")]
    [Trait("Category", "SadPath")]
    public async Task Test_GpuKernelDirectInvocation_ReportsGSH0031()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/GSH-31_DirectGpuKernelInvocation.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0031");
    }
}
