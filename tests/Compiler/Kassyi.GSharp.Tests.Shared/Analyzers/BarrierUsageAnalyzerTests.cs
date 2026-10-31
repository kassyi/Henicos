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
    public async Task Test_DivergentBarrier_IsAWarning()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/DivergentBarrierKernels.cu.cs");

        // Every divergent branch is a warning, also tid % 2: whether it diverges depends on the launch (#772 review)
        Assert.Equal(
            [
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 14, 13, 14, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 32, 13, 32, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 47, 17, 47, 34)
            ],
            actual);
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

    // The example of the GSH0040 page: the jump crosses a call to a device function that reaches a barrier
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_GotoAcrossSuspendingCall_ReportsGSH0040OnGoto()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/BarrierUsage/CrossSuspendingCallGotoKernels.cu.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var diagnostic = Assert.Single(diagnostics, static d => d.Id == GSharpDiagnostics.CrossBarrierGotoProhibited.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var span = diagnostic.Location.GetLineSpan();
        Assert.Equal((20, 12), (span.StartLinePosition.Line, span.StartLinePosition.Character));
        Assert.Equal((20, 22), (span.EndLinePosition.Line, span.EndLinePosition.Character));
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
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_ByRefAndRefStructKernelParameters_HasNoDiagnostics()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/KernelParameterRefStructKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
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
    public async Task Test_DivergentCallToBarrierDeviceFunction_ReportsGSH0083()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/DivergentSuspendingCallKernels.cu.cs");

        // tid == 0 diverges in any launch with two threads in X, but not in one whose X width is 1: a warning
        Assert.Equal(
            [
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 20, 13, 20, 27)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DivergentBarrierInDeviceFunction_ReportsGSH0083()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/DivergentBarrierInDeviceFunctionKernels.cu.cs");

        // tid < 16 depends on the block width
        Assert.Equal(
            [
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 13, 13, 13, 30)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DivergentCallReachingBarrierTwoLevelsDeep_ReportsGSH0083()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/DivergentNestedSuspendingCallKernels.cu.cs");

        // tid < 16 depends on the block width
        Assert.Equal(
            [
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 26, 13, 26, 32)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_BarrierDeviceFunctionOnRightOfShortCircuit_ReportsGSH0083()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/ShortCircuitSuspendingCallKernels.cu.cs");

        // tid < 16 depends on the block width
        Assert.Equal(
            [
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 19, 43, 19, 59)
            ],
            actual);
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
    public async Task Test_ThreadVariantConditions_AreWarnings()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/ThreadVariantConditionKernels.cu.cs");

        // One warning per kernel: a derived local (lane == 0), a conditionally assigned local, a thread-stride loop and
        // Gpu.LaneId() < 16
        Assert.Equal(
            [
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 16, 13, 16, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 36, 13, 36, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 51, 13, 51, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 65, 13, 65, 30)
            ],
            actual);
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

    // A reference into the result of a call cannot be kept across a resume, also through a member access, parentheses or
    // an in parameter without the keyword (#788 review); variables stay allowed
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_RefReturnMemberArgument_ReportsGSH0062AtTheArgument()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/RefReturnMemberArgumentKernels.cu.cs");

        Assert.Equal(
            [
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 38, 25, 38, 51),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 39, 25, 39, 53),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 41, 28, 41, 45)
            ],
            actual);
    }

    // A field of a value passed to an in parameter without the keyword is a copy, and ref this.F names a field of the
    // struct itself; neither is a by-reference argument that GSH0062 reports (#850 review)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_ValueFieldOrOwnFieldArgument_DoesNotReportGSH0062()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/ValueFieldInArgumentKernels.cu.cs");

        Assert.DoesNotContain(actual, static d => d.Id == GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id);
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

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_UserDefinedGpuType_IsNotTakenForTheBarrier()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/LookalikeGpuTypeKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UserDefinedGpuKernelAttribute_IsNotTakenForAKernel()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/LookalikeGpuKernelAttributeKernels.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();

        // The barrier is reported as called from host code, not as divergent inside a kernel
        Assert.Contains(diagnostics, d => d.Id == "GSH0042");
        Assert.DoesNotContain(diagnostics, d => d.Id == "GSH0037");
    }

    private static async Task<IReadOnlyList<(string Id, int StartLine, int StartCol, int EndLine, int EndCol)>> RunAsync(
        string relativeTestCasePath)
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer(relativeTestCasePath, s_analyzer).GetDiagnosticsAsync();

        return diagnostics
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Select(static d => (d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .ToList();
    }
}
