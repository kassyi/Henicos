using System;
using System.Globalization;
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
    public async Task UniformBarriers_SynchronizedKernel_HasNoDiagnostics()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/UniformBarrierKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task DivergentBarrier_ThreadIdBranch_ReportsWarning()
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
    public async Task ExceptionHandling_InKernel_ReportsGSH0038()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/ExceptionHandlingInKernelKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0038");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task CrossBarrierRefStruct_VariableDeclaration_ReportsGSH0039()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/CrossBarrierRefStructKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0039");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task CrossBarrierGoto_Statement_ReportsGSH0040()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/CrossBarrierGotoKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0040");
    }

    // The example of the GSH0040 page: the jump crosses a call to a device function that reaches a barrier
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task GotoAcrossSuspendingCall_Statement_ReportsGSH0040OnGoto()
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
    public async Task CrossBarrierLocalPointer_Declaration_ReportsGSH0041()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/CrossBarrierLocalPointerKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0041");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ExternalBarrierCall_DirectCall_ReportsGSH0042()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/ExternalBarrierCallKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0042");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task FixedBlockBarrier_Statement_ReportsGSH0043()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/FixedBlockBarrierKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0043");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task ByRefAndRefStruct_KernelParameters_HasNoDiagnostics()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/KernelParameterRefStructKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task RecursiveBarrier_DirectOrIndirectCall_ReportsGSH0045()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/RecursiveLocalFunctionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0045");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task DeviceFunctionBarriers_NonRecursiveCalls_HasNoDiagnostics()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/NonRecursiveDeviceFunctionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task DirectlyRecursiveDeviceFunctionBarrier_CallCycle_ReportsGSH0061()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/RecursiveDeviceFunctionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0061");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task MutuallyRecursiveDeviceFunctionBarrier_MutualRecursion_ReportsGSH0061()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/MutuallyRecursiveDeviceFunctionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0061");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task DivergentCallToBarrierDeviceFunction_DivergentBranch_ReportsGSH0083()
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
    public async Task DivergentBarrierInDeviceFunction_BranchInsideCallee_ReportsGSH0083()
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
    public async Task DivergentCallReachingBarrierTwoLevelsDeep_NestedCalls_ReportsGSH0083()
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
    public async Task BarrierDeviceFunctionOnRightOfShortCircuit_ShortCircuitExpression_ReportsGSH0083()
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
    public async Task RefStructAcrossSuspendingCallInDeviceFunction_DeviceScope_ReportsGSH0039()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/CrossSuspendingCallRefStructKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0039");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ThreadVariantConditions_ThreadDependentBranches_ReportsWarnings()
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
    public async Task UniformConditions_ThreadInvariantExpressions_HasNoDiagnostics()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/UniformConditionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task DeviceFunctionRefParametersAndWarpOnlyBranch_UniformExecution_HasNoDiagnostics()
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
    public async Task SuspendingCallInUnsupportedContext_ConditionalBranch_ReportsGSH0062(string testCase)
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/" + testCase, s_analyzer)
                               .ReportsErrorsAsync("GSH0062");
    }

    // A warp shuffle is a split point of the CPU state machine only in the statements of the body, the operands of '?:',
    // '&&' and '||' and a Gpu.ForUnroll or Gpu.GridStrideLoop body; anywhere else it is reported like a call that
    // suspends (#1274)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task WarpShuffleInUnsplitContext_CpuDialect_ReportsGSH0062()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/WarpShuffleInUnsplitContextKernels.cu.cs", "Cpu");

        Assert.Equal(
            [
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 24, 22, 24, 66),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 38, 45, 38, 84),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 60, 18, 60, 70),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 72, 25, 72, 65),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 83, 30, 83, 75),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 92, 31, 92, 74)
            ],
            actual);

        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/BarrierUsage/WarpShuffleInUnsplitContextKernels.cu.cs", s_analyzer)
            .WithDialect("Cpu")
            .GetDiagnosticsAsync();
        Assert.All(diagnostics, static d => Assert.Contains("' is a warp shuffle, so it cannot be called ",
            d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    // A using or fixed statement and the scope of a using declaration are not flattened by the CPU lowering, so a warp
    // shuffle, vote or reduction there is not split (#1172); a vote in a local function is not split either (#845). A
    // Gpu.GridStrideLoop body is (#1091)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task WarpExchangeInScope_CpuDialect_ReportsGSH0062()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/WarpExchangeInScopeKernels.cu.cs", "Cpu");

        Assert.Equal(
            [
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 28, 17, 28, 62),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 40, 23, 40, 75),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 50, 22, 50, 58),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 58, 31, 58, 70)
            ],
            actual);

        var messages = (await RoslynTestUtility
                .TestAnalyzer("Analyzers/BarrierUsage/WarpExchangeInScopeKernels.cu.cs", s_analyzer)
                .WithDialect("Cpu")
                .GetDiagnosticsAsync())
            .OrderBy(static d => d.Location.SourceSpan.Start)
            .Select(static d => d.GetMessage(CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Contains("in a using statement", messages[0], StringComparison.Ordinal);
        Assert.Contains("in the scope of a using declaration", messages[1], StringComparison.Ordinal);
        Assert.Contains("in a fixed statement", messages[2], StringComparison.Ordinal);
        Assert.Contains("in a local function", messages[3], StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task WarpExchangeInScope_CudaDialect_ProducesNoDiagnostics()
    {
        Assert.Empty(await RunAsync("Analyzers/BarrierUsage/WarpExchangeInScopeKernels.cu.cs", "Cuda"));
    }

    // CUDA runs a warp shuffle where it stands, so a project built only for CUDA has no CPU path to split and accepts
    // every shuffle of the same kernels
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task WarpShuffleInUnsplitContext_CudaDialect_ProducesNoDiagnostics()
    {
        Assert.Empty(await RunAsync("Analyzers/BarrierUsage/WarpShuffleInUnsplitContextKernels.cu.cs", "Cuda"));
    }

    // A reference into the result of a call cannot be kept across a resume, also through a member access, parentheses or
    // an in parameter without the keyword (#788 review), nor a ref conditional, with or without the keyword and in
    // parentheses, nor an element of an inline array that a call returns (#1585; #927 is to keep the reference
    // instead). Variables stay allowed: an element of an inline array local, and what a pointer points to
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task RefReturnMemberArgument_SuspendingCall_ReportsGSH0062AtArgument()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/RefReturnMemberArgumentKernels.cu.cs");

        Assert.Equal(
            [
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 38, 25, 38, 51),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 39, 25, 39, 53),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 41, 28, 41, 45),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 52, 28, 52, 49),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 53, 28, 53, 51),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 54, 25, 54, 50),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 60, 28, 60, 53),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 61, 28, 61, 56)
            ],
            actual);
    }

    // A field of a value passed to an in parameter without the keyword is a copy, and ref this.F names a field of the
    // struct itself; neither is a by-reference argument that GSH0062 reports (#850 review)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task ValueFieldOrOwnFieldArgument_SuspendingCall_DoesNotReportGSH0062()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/ValueFieldInArgumentKernels.cu.cs");

        Assert.DoesNotContain(actual, static d => d.Id == GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task AsyncYieldInKernel_AwaitOrYield_ReportsGSH0046()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/AsyncYieldInKernelKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0046");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task LockStatementInKernel_LockSyntax_ReportsGSH0047()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/LockStatementInKernelKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0047");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task UserDefinedGpuType_CustomTypeBarrierCall_IsNotTreatedAsBarrier()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/LookalikeGpuTypeKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task UserDefinedGpuKernelAttribute_CustomAttribute_IsNotTreatedAsKernel()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/BarrierUsage/LookalikeGpuKernelAttributeKernels.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();

        // The barrier is reported as called from host code, not as divergent inside a kernel
        Assert.Contains(diagnostics, d => d.Id == "GSH0042");
        Assert.DoesNotContain(diagnostics, d => d.Id == "GSH0037");
    }

    private static async Task<IReadOnlyList<(string Id, int StartLine, int StartCol, int EndLine, int EndCol)>> RunAsync(
        string relativeTestCasePath, string dialect = "Cuda")
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer(relativeTestCasePath, s_analyzer)
            .WithDialect(dialect)
            .GetDiagnosticsAsync();

        return diagnostics
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Select(static d => (d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .ToList();
    }
}
