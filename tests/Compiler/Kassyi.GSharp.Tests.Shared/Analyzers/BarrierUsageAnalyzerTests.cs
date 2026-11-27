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
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/UniformBarrierKernels.cu.cs", s_analyzer)
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
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/ExceptionHandlingInKernelKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0038");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task CrossBarrierRefStruct_VariableDeclaration_ReportsGSH0039()
    {
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/CrossBarrierRefStructKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0039");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task CrossBarrierGoto_Statement_ReportsGSH0040()
    {
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/CrossBarrierGotoKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0040");
    }

    // The example of the GSH0040 page: the jump crosses a call to a device function that reaches a barrier
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task GotoAcrossSuspendingCall_Statement_ReportsGSH0040OnGoto()
    {
        var diagnostics = await GeneratorTestRunner
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
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/CrossBarrierLocalPointerKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0041");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ExternalBarrierCall_DirectCall_ReportsGSH0042()
    {
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/ExternalBarrierCallKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0042");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task FixedBlockBarrier_Statement_ReportsGSH0043()
    {
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/FixedBlockBarrierKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0043");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task ByRefAndRefStruct_KernelParameters_HasNoDiagnostics()
    {
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/KernelParameterRefStructKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task RecursiveBarrier_DirectOrIndirectCall_ReportsGSH0045()
    {
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/RecursiveLocalFunctionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0045");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task DeviceFunctionBarriers_NonRecursiveCalls_HasNoDiagnostics()
    {
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/NonRecursiveDeviceFunctionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task DirectlyRecursiveDeviceFunctionBarrier_CallCycle_ReportsGSH0061()
    {
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/RecursiveDeviceFunctionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0061");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task MutuallyRecursiveDeviceFunctionBarrier_MutualRecursion_ReportsGSH0061()
    {
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/MutuallyRecursiveDeviceFunctionBarrierKernels.cu.cs", s_analyzer)
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
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/CrossSuspendingCallRefStructKernels.cu.cs", s_analyzer)
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
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/UniformConditionBarrierKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task DeviceFunctionRefParametersAndWarpOnlyBranch_UniformExecution_HasNoDiagnostics()
    {
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/DeviceFunctionRefParameterBarrierKernels.cu.cs", s_analyzer)
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
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/" + testCase, s_analyzer)
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

        var diagnostics = await GeneratorTestRunner
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

        var messages = (await GeneratorTestRunner
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

    // The CPU paths hoist a split in the initializer of a creation with a collection initializer to the start of the
    // statement, before the values, Add calls and constructor that C# evaluates first, also in an object initializer
    // around or inside the collection; CUDA does not split. Until the CPU paths run one stack per GPU thread (#2105)
    // such a split is reported, and a split that is the first value of its creation (a member value or an element
    // creation included, after constants only) is not
    private const string CollectionInitializerKernels =
        "Analyzers/BarrierUsage/SuspendingCallInCollectionInitializerKernels.cu.cs";

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task SuspendingCallInCollectionInitializer_ValueBeforeTheCall_ReportsGSH0062()
    {
        var actual = await RunAsync(CollectionInitializerKernels, "Cpu");

        Assert.Equal(
            [
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 83, 41, 83, 53),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 84, 51, 84, 63),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 85, 47, 85, 59),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 86, 68, 86, 80),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 87, 47, 87, 59),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 88, 42, 88, 77),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 89, 74, 89, 86),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 90, 85, 90, 97),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 91, 71, 91, 83),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 92, 72, 92, 84)
            ],
            actual);

        var diagnostics = await GeneratorTestRunner
            .TestAnalyzer(CollectionInitializerKernels, s_analyzer)
            .WithDialect("Cpu")
            .GetDiagnosticsAsync();
        Assert.All(diagnostics, static d => Assert.Contains(
            "in the initializer of a creation with a collection initializer, where C# evaluates another value",
            d.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal));
    }

    // A call of a device function that reaches a barrier is reported for every target; a warp shuffle written in
    // place only where the CPU paths are built
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task SuspendingCallInCollectionInitializer_CudaDialect_ReportsDeviceFunctionCallsOnly()
    {
        var actual = await RunAsync(CollectionInitializerKernels, "Cuda");

        Assert.Equal(
            [
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 83, 41, 83, 53),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 84, 51, 84, 63),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 85, 47, 85, 59),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 86, 68, 86, 80),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 87, 47, 87, 59),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 89, 74, 89, 86),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 90, 85, 90, 97),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 91, 71, 91, 83),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 92, 72, 92, 84)
            ],
            actual);
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
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/AsyncYieldInKernelKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0046");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task LockStatementInKernel_LockSyntax_ReportsGSH0047()
    {
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/LockStatementInKernelKernels.cu.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0047");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task UserDefinedGpuType_CustomTypeBarrierCall_IsNotTreatedAsBarrier()
    {
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/LookalikeGpuTypeKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task UserDefinedGpuKernelAttribute_CustomAttribute_IsNotTreatedAsKernel()
    {
        var diagnostics = await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/LookalikeGpuKernelAttributeKernels.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();

        // The barrier is reported as called from host code, not as divergent inside a kernel
        Assert.Contains(diagnostics, d => d.Id == "GSH0042");
        Assert.DoesNotContain(diagnostics, d => d.Id == "GSH0037");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ReconvergenceJump_GotoIntoMiddle_ReportsGSH0120OnGoto()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/ReconvergenceJumpKernels.cu.cs");

        Assert.Equal(
            [
                ("GSH0120", 25, 13, 25, 22),
                ("GSH0120", 39, 13, 39, 22),
                ("GSH0120", 64, 21, 64, 30),
                ("GSH0120", 101, 13, 101, 22),
                ("GSH0120", 113, 13, 113, 22),
                ("GSH0120", 139, 21, 139, 30),
                ("GSH0120", 168, 21, 168, 33),
                ("GSH0120", 196, 17, 196, 29),
                ("GSH0120", 213, 17, 213, 29),
                ("GSH0120", 215, 17, 215, 29),
                ("GSH0120", 232, 21, 232, 30),
                ("GSH0120", 270, 21, 270, 32),
                ("GSH0120", 299, 21, 299, 33)
            ],
            actual);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ReconvergenceJump_Generator_ReportsOnlyGSH0120WithoutInternalError(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/BarrierUsage/ReconvergenceJumpKernels.cu.cs"));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.Equal(
            [
                (DiagnosticOrigin.Analyzer, 25, 13, 25, 22),
                (DiagnosticOrigin.Analyzer, 39, 13, 39, 22),
                (DiagnosticOrigin.Analyzer, 64, 21, 64, 30),
                (DiagnosticOrigin.Analyzer, 101, 13, 101, 22),
                (DiagnosticOrigin.Analyzer, 113, 13, 113, 22),
                (DiagnosticOrigin.Analyzer, 139, 21, 139, 30),
                (DiagnosticOrigin.Analyzer, 168, 21, 168, 33),
                (DiagnosticOrigin.Analyzer, 196, 17, 196, 29),
                (DiagnosticOrigin.Analyzer, 213, 17, 213, 29),
                (DiagnosticOrigin.Analyzer, 215, 17, 215, 29),
                (DiagnosticOrigin.Analyzer, 232, 21, 232, 30),
                (DiagnosticOrigin.Analyzer, 270, 21, 270, 32),
                (DiagnosticOrigin.Analyzer, 299, 21, 299, 33)
            ],
            run.Analyzer
                .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
                .ToImmutableArray()
                .Ranges("GSH0120")
                .OrderBy(static r => r.StartLine)
                .ToList());
        Assert.Equal(13, run.Analyzer.Length);
        Assert.Empty(run.Generator);
        Assert.Empty(KernelDiagnostics.FindStatedFailures(run));
        var inputTrees = run.Input.SyntaxTrees.ToHashSet();
        Assert.All(
            run.Output.SyntaxTrees.Where(tree => !inputTrees.Contains(tree)),
            static tree => Assert.DoesNotContain(
                tree.GetRoot().DescendantTrivia(),
                static trivia => trivia.IsKind(SyntaxKind.ErrorDirectiveTrivia)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task ReconvergenceJump_ToLoopHeadOrWithoutActiveMask_HasNoDiagnostics()
    {
        await GeneratorTestRunner.TestAnalyzer("Analyzers/BarrierUsage/ReconvergenceJumpValidKernels.cu.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task ReconvergenceJumpValid_Generator_ReportsNoDiagnostics(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/BarrierUsage/ReconvergenceJumpValidKernels.cu.cs"));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.Empty(run.Analyzer);
        Assert.Empty(run.Generator);
        Assert.Empty(KernelDiagnostics.FindStatedFailures(run));
        var inputTrees = run.Input.SyntaxTrees.ToHashSet();
        Assert.All(
            run.Output.SyntaxTrees.Where(tree => !inputTrees.Contains(tree)),
            static tree => Assert.DoesNotContain(
                tree.GetRoot().DescendantTrivia(),
                static trivia => trivia.IsKind(SyntaxKind.ErrorDirectiveTrivia)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ReconvergenceJump_NestedFunctionWithMiddleGoto_ReportsOnlyGSH0062WithoutGSH0120()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/ReconvergenceJumpNestedFunctionKernels.cu.cs", "Cpu");

        Assert.Equal(
            [
                ("GSH0062", 19, 17, 19, 34),
                ("GSH0062", 47, 17, 47, 34),
                ("GSH0062", 75, 17, 75, 34),
                ("GSH0062", 101, 30, 101, 47),
                ("GSH0062", 103, 40, 103, 57)
            ],
            actual);
        Assert.DoesNotContain(actual, static d => d.Id == "GSH0120");
        Assert.Empty(await RunAsync("Analyzers/BarrierUsage/ReconvergenceJumpNestedFunctionKernels.cu.cs", "Cuda"));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ChainedLabelJump_GotoToInnerLabelFromAnotherState_ReportsGSH0121OnceFromTheGeneratorOnly()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/BarrierUsage/ChainedLabelGotoKernels.cu.cs"));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu");

        var id = GSharpDiagnostics.GotoToChainedLabelAcrossStates.Id;
        // An IDE shows the diagnostics of the analyzer and of the generator side by side and merges none, so a goto
        // reported from both places is seen twice (#2121): the generator alone reports it, once per goto
        // mutant: KernelBarrierValidator calling a Validate of GSH0121 again -> the analyzer reports every goto too
        // mutant: FindGotosToChainedLabelsAcrossStates `!=` -> `==` -> none of these, and the same-state goto instead
        // mutant: the chain walk stops after one inner label -> no report at line 59
        // mutant: NumberStates with an empty set of state labels -> no report at line 85
        Assert.Empty(run.Analyzer.Where(d => d.Id == id));
        Assert.Equal(
            [
                (DiagnosticOrigin.Generator, 21, 13, 21, 25),
                (DiagnosticOrigin.Generator, 35, 13, 35, 25),
                (DiagnosticOrigin.Generator, 59, 13, 59, 24),
                (DiagnosticOrigin.Generator, 85, 13, 85, 25),
                (DiagnosticOrigin.Generator, 102, 13, 102, 25)
            ],
            GeneratorRanges(run, id));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ChainedLabelJump_Generator_ReportsGSH0121AndTheGeneratedCSharpCompiles()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/BarrierUsage/ChainedLabelGotoKernels.cu.cs"));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu");

        // The generator reports the diagnostics from its own lowering, so a build without the analyzers stops too
        // mutant: HeldRefLocalSite.Collect without the GSH0121 sites -> the generator reports nothing
        Assert.Empty(run.Analyzer);
        Assert.Equal(5, run.Generator.Length);
        Assert.Empty(KernelDiagnostics.FindStatedFailures(run));
        // The managed output of a rejected method is not written: no CS0159 is generated
        // mutant: ManagedCSharpTranspiler.CreateBody without the GotosToChainedLabelsAcrossStates branch -> CS0159
        Assert.Empty(run.Output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString()));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task ChainedLabelJump_CudaDialectExported_ReportsTheDeviceFunctionOnly()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/BarrierUsage/ChainedLabelGotoKernels.cu.cs"));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cuda");

        // A CUDA project exports its device code by default: its consumers on the CPU paths run the managed state
        // machine of the device function, which the rejected jump leaves empty. The kernels run on the GPU as written
        // mutant: ReportHeldRefLocals with the kernels' sites on CUDA -> the generator reports the kernels too
        var id = GSharpDiagnostics.GotoToChainedLabelAcrossStates.Id;
        Assert.Empty(run.Analyzer.Where(d => d.Id == id));
        Assert.Equal([(DiagnosticOrigin.Generator, 102, 13, 102, 25)], GeneratorRanges(run, id));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ChainedLabelJump_GotoAcrossBarrier_ReportsGSH0040AloneAndGSH0121FromTheGeneratorOnly()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/BarrierUsage/ChainedLabelGotoBarrierKernels.cu.cs"));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu");

        // A goto across a barrier or a call that suspends gets GSH0040 only; one across a shuffle alone keeps GSH0121,
        // though the method has a barrier before the chain. The generator reports both, so a build without the
        // analyzers stops at the same places; GSH0121 comes from the generator alone
        // mutant: FindReported without the IsCrossBarrierGoto check -> GSH0121 at lines 31 and 50 too
        // mutant: IsCrossBarrierGoto given the goto of the lowered body -> GSH0121 at lines 31 and 50 too
        // mutant: GSH0121 left out whenever the method has a barrier -> no GSH0121 at line 70
        // mutant: HeldRefLocalSite.Report without the CrossesBarrier branch -> the generator reports GSH0121 at 31 and 50
        var gsh0040 = GSharpDiagnostics.CrossBarrierGotoProhibited.Id;
        var gsh0121 = GSharpDiagnostics.GotoToChainedLabelAcrossStates.Id;
        var analyzer = run.Analyzer
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .ToImmutableArray();
        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 31, 13, 31, 25), (DiagnosticOrigin.Analyzer, 50, 13, 50, 25)],
            analyzer.Ranges(gsh0040).OrderBy(static r => r.StartLine).ToList());
        Assert.Empty(analyzer.Ranges(gsh0121));
        Assert.Equal([(DiagnosticOrigin.Generator, 70, 13, 70, 25)], GeneratorRanges(run, gsh0121));
        Assert.Equal(3, run.Generator.Length);
        Assert.True(run.Analyzer
            .Where(d => d.Id == gsh0040)
            .ToHashSet()
            .SetEquals(run.Generator.Where(d => d.Id == gsh0040)));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task ChainedLabelJump_CudaDialectNotExported_ReportsNothing()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/BarrierUsage/ChainedLabelGotoKernels.cu.cs"));

        // Without the export no managed state machine is emitted, so the CUDA jump is left as written
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cuda",
            new Dictionary<string, string> { ["build_property.GSharpExportDeviceCode"] = "false" });

        Assert.DoesNotContain(run.Analyzer, static d => d.Id == GSharpDiagnostics.GotoToChainedLabelAcrossStates.Id);
        Assert.Empty(run.Generator);
        Assert.Empty(KernelDiagnostics.FindStatedFailures(run));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task ChainedLabelJumpValid_Generator_ReportsNoDiagnostics(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/BarrierUsage/ChainedLabelGotoValidKernels.cu.cs"));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        // Same state, first label, a label with a statement of its own, and an unroll hint before chained labels: no
        // GSH0121, and no GSH0017 stated as #error in the CPU C++ output (which splitting the chain would give)
        // mutant: FindGotosToChainedLabelsAcrossStates without the state comparison -> GSH0121 on the same-state goto
        Assert.Empty(run.Analyzer);
        Assert.Empty(run.Generator);
        Assert.Empty(KernelDiagnostics.FindStatedFailures(run));
        Assert.Empty(run.Output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString()));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task SameNameLabels_GotoToTheLabelOfItsOwnBlock_JudgedByThatLabel()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/BarrierUsage/SameNameLabelGotoKernels.cu.cs"));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu");

        // Sibling blocks may declare one label name. A goto is measured against the label C# binds it to, so a barrier
        // between the blocks is crossed by neither goto; one inside the goto's own block still is (GSH0040), and a
        // chained goto across a shuffle alone stays GSH0121, from the generator
        // mutant: CrossBarrierChecks.FindTargetLabel by name -> GSH0040 at lines 31, 91 and 114 too
        // mutant: the nearest label of the name before the goto -> GSH0040 at line 114 (the forward goto)
        // mutant: FindReported finding the label by name -> no GSH0121 at line 91, the generator reports GSH0040 there
        var gsh0040 = GSharpDiagnostics.CrossBarrierGotoProhibited.Id;
        var gsh0121 = GSharpDiagnostics.GotoToChainedLabelAcrossStates.Id;
        var analyzer = run.Analyzer
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .ToImmutableArray();
        Assert.Equal([(DiagnosticOrigin.Analyzer, 59, 17, 59, 28)], analyzer.Ranges(gsh0040).ToList());
        Assert.Empty(analyzer.Ranges(gsh0121));
        Assert.Equal([(DiagnosticOrigin.Generator, 91, 17, 91, 29)], GeneratorRanges(run, gsh0121));
        Assert.Single(run.Generator);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task SuspendingCallContext_LambdaAndLocalFunction_ReportsGSH0062OnlyThere(string dialect)
    {
        const string testCase = "Analyzers/BarrierUsage/SuspendingCallContextKernels.cu.cs";
        var actual = await RunAsync(testCase, dialect);

        Assert.Equal(
            [
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 23, 26, 23, 33),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 31, 21, 31, 28)
            ],
            actual);

        var messages = (await GeneratorTestRunner
                .TestAnalyzer(testCase, s_analyzer)
                .WithDialect(dialect)
                .GetDiagnosticsAsync())
            .OrderBy(static d => d.Location.SourceSpan.Start)
            .Select(static d => d.GetMessage(CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Contains("in a lambda or anonymous method", messages[0], StringComparison.Ordinal);
        Assert.Contains("in a local function", messages[1], StringComparison.Ordinal);
    }

    private static List<(DiagnosticOrigin Origin, int StartLine, int StartColumn, int EndLine, int EndColumn)>
        GeneratorRanges(KernelDiagnosticsRun run, string id) =>
        run.Generator
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Generator, d))
            .ToImmutableArray()
            .Ranges(id)
            .OrderBy(static r => r.StartLine)
            .ToList();

    private static async Task<IReadOnlyList<(string Id, int StartLine, int StartCol, int EndLine, int EndCol)>> RunAsync(
        string relativeTestCasePath, string dialect = "Cuda")
    {
        var diagnostics = await GeneratorTestRunner
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
