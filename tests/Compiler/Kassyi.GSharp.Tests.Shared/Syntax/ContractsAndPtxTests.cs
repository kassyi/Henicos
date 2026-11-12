using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class ContractsAndPtxTests : TranspilerTestBase
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void InlinePtx_KernelLaunch_WritesExpectedValueOrThrowsNotSupportedException()
    {
        uint[] data = [0];
        if (CurrentDialect.Capabilities.HasFlag(DialectCapabilities.InlineAssembly))
        {
            ContractsAndPtxKernels.LaunchInlinePtxKernel(data);
            Assert.Equal(999u, data[0]);
        }
        else
        {
            var ex = Assert.Throws<NotSupportedException>(
                () => ContractsAndPtxKernels.LaunchInlinePtxKernel(data));
            Assert.Contains("Gpu.InlinePtx", ex.Message);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void InlinePtx_FollowedByWriteOnCpuDialect_HaltsBeforeSubsequentStatements()
    {
        Skip.If(IsCudaDialect, "This test verifies CPU-dialect halts before subsequent statements after Gpu.InlinePtx.");

        uint[] data = [42u];
        var ex = Assert.Throws<NotSupportedException>(
            () => ContractsAndPtxKernels.LaunchInlinePtxFollowedByWriteKernel(data));
        Assert.Contains("Gpu.InlinePtx", ex.Message);
        Assert.Equal(42u, data[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void InlinePtx_MultiThreadCpuLaunch_PropagatesSingleNotSupportedException()
    {
        Skip.If(IsCudaDialect, "This test verifies CPU-dialect OpenMP parallel error propagation.");

        uint[] data = new uint[256];
        var ex = Assert.Throws<NotSupportedException>(
            () => ContractsAndPtxKernels.LaunchInlinePtxKernel(data, gridDimX: 4, blockDimX: 64));
        Assert.Contains("Gpu.InlinePtx", ex.Message);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void InlinePtx_ManagedFallbackLaunch_PropagatesNotSupportedException()
    {
        Skip.If(IsCudaDialect, "This test verifies CPU-dialect managed fallback error propagation.");

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            uint[] data = [0];
            var ex = Assert.Throws<NotSupportedException>(
                () => ContractsAndPtxKernels.LaunchInlinePtxKernel(data));
            Assert.Contains("Gpu.InlinePtx", ex.Message);
        }
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_ConstexprPropagation_EmitsFoldedConstantValue()
    {
        var (cudaSource, diagnostics) = TranspileTestRunner.TranspileMethod<ContractsAndPtxTarget>(
            nameof(ContractsAndPtxTarget.ConstexprPropagation), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        // TileSize * 2 is a constant, written as its value (#722); the target is not a [GpuKernel], so its array
        // parameter may be null and the write is checked (#706)
        Assert.Matches(@"__gsharp_array_at\(\w*output, 0, ""[^""]*""\)=64;", cudaSource);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_WmmaTensorCores_EmitsPtxOrUnsupportedComment()
    {
        var (cudaSource, diagnostics) = TranspileTestRunner.TranspileMethod<ContractsAndPtxTarget>(
            nameof(ContractsAndPtxTarget.WmmaTensorCores), dialect: CurrentDialect);

        // The analyzer warns about the call in the CPU dialect (DialectDiagnosticsTests); the transpiler reports nothing
        Assert.Empty(diagnostics);
        if (CurrentDialect.Capabilities.HasFlag(DialectCapabilities.InlineAssembly))
        {
            Assert.Contains("// wmma.mma.sync.aligned.row.col.m16n16k16.f32.f32", cudaSource);
        }
        else
        {
            Assert.Contains("__gsharp_unsupported(", cudaSource);
            Assert.DoesNotContain("wmma.mma", cudaSource);
        }
    }
}

public class ContractsAndPtxTarget
{
    public const int TileSize = 32;

    public static void ConstexprPropagation(int[] output)
    {
        output[0] = TileSize * 2;
    }

    public static void WmmaTensorCores(uint[] output)
    {
        Gpu.InlinePtx("// wmma.mma.sync.aligned.row.col.m16n16k16.f32.f32");
        output[0] = 16u;
    }
}


