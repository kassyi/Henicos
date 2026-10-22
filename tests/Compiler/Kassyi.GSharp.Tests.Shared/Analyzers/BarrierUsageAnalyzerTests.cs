using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class BarrierUsageAnalyzerTests : TranspilerTestBase
{
    private static readonly BarrierUsageAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_UniformBarriers_HappyPath_HasNoDiagnostics()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/UniformBarrierKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DivergentBarrier_ReportsGSH0037()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/DivergentBarrierKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0037");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ExceptionHandlingInKernel_ReportsGSH0038()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/ExceptionHandlingInKernelKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0038");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CrossBarrierRefStruct_ReportsGSH0039()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/CrossBarrierRefStructKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0039");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CrossBarrierGoto_ReportsGSH0040()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/CrossBarrierGotoKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0040");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CrossBarrierLocalPointer_ReportsGSH0041()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/CrossBarrierLocalPointerKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0041");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ExternalBarrierCall_ReportsGSH0042()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/ExternalBarrierCallKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0042");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_FixedBlockBarrier_ReportsGSH0043()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/FixedBlockBarrierKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0043");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_InvalidKernelParameters_ReportsGSH0044()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/KernelParameterRefStructKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0044");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_RecursiveBarrier_ReportsGSH0045()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/RecursiveLocalFunctionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0045");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_DeviceFunctionBarriers_HappyPath_HasNoDiagnostics()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/NonRecursiveDeviceFunctionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DirectlyRecursiveDeviceFunctionBarrier_ReportsGSH0061()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/RecursiveDeviceFunctionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0061");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_MutuallyRecursiveDeviceFunctionBarrier_ReportsGSH0061()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/MutuallyRecursiveDeviceFunctionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0061");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DivergentCallToBarrierDeviceFunction_ReportsGSH0037()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/DivergentSuspendingCallKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0037");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DivergentBarrierInDeviceFunction_ReportsGSH0037()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/DivergentBarrierInDeviceFunctionKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0037");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DivergentCallReachingBarrierTwoLevelsDeep_ReportsGSH0037()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/DivergentNestedSuspendingCallKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0037");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_BarrierDeviceFunctionOnRightOfShortCircuit_ReportsGSH0037()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/ShortCircuitSuspendingCallKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0037");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_RefStructAcrossSuspendingCallInDeviceFunction_ReportsGSH0039()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/CrossSuspendingCallRefStructKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0039");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ThreadVariantConditions_ReportGSH0037()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/ThreadVariantConditionKernels.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();

        // One report per kernel: a derived local, a conditionally assigned local, a thread-stride loop, the lane id
        var divergent = 0;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Id == "GSH0037")
            {
                divergent++;
            }
        }

        Assert.Equal(4, divergent);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_UniformConditions_HasNoDiagnostics()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/UniformConditionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_DeviceFunctionRefParametersAndWarpOnlyBranch_HasNoDiagnostics()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/DeviceFunctionRefParameterBarrierKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("SuspendingCallInLambdaKernels.cu.cs")]
    [InlineData("SuspendingCallInLocalFunctionKernels.cu.cs")]
    [InlineData("SuspendingCallInSwitchExpressionKernels.cu.cs")]
    [InlineData("RefReturnArgumentKernels.cu.cs")]
    public async Task Test_SuspendingCallInUnsupportedContext_ReportsGSH0062(string testCase)
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/" + testCase, s_analyzer)
                               .ReportsErrorsAsync("GSH0062");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_AsyncYieldInKernel_ReportsGSH0046()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/AsyncYieldInKernelKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0046");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_LockStatementInKernel_ReportsGSH0047()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/LockStatementInKernelKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0047");
    }
}
