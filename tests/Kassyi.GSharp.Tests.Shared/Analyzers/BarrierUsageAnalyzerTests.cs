using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class BarrierUsageAnalyzerTests : TranspilerTestBase
{
    private static readonly BarrierUsageAnalyzer s_analyzer = new();

    [Fact]
    [Trait("Category", "HappyPath")]
    public async Task Test_UniformBarriers_HappyPath_HasNoDiagnostics()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/HappyPath_UniformBarriers.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public async Task Test_DivergentBarrier_ReportsGSH0037()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/SadPath_GSH0037_Divergent.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0037");
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public async Task Test_ExceptionHandlingInKernel_ReportsGSH0038()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/SadPath_GSH0038_Exception.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0038");
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public async Task Test_CrossBarrierRefStruct_ReportsGSH0039()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/SadPath_GSH0039_RefStruct.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0039");
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public async Task Test_CrossBarrierGoto_ReportsGSH0040()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/SadPath_GSH0040_Goto.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0040");
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public async Task Test_CrossBarrierLocalPointer_ReportsGSH0041()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/SadPath_GSH0041_LocalPointer.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0041");
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public async Task Test_ExternalBarrierCall_ReportsGSH0042()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/SadPath_GSH0042_ExternalMethod.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0042");
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public async Task Test_FixedBlockBarrier_ReportsGSH0043()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/SadPath_GSH0043_FixedBlock.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0043");
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public async Task Test_InvalidKernelParameters_ReportsGSH0044()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/SadPath_GSH0044_InvalidParams.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0044");
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public async Task Test_RecursiveBarrier_ReportsGSH0045()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/SadPath_GSH0045_RecursiveBarrier.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0045");
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public async Task Test_AsyncYieldInKernel_ReportsGSH0046()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/SadPath_GSH0046_AsyncYield.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0046");
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public async Task Test_LockStatementInKernel_ReportsGSH0047()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/SadPath_GSH0047_Lock.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0047");
    }
}
