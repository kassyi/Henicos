using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class ContractsAndPtxTests : TranspilerTestBase
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestInlinePtxKernel()
    {
        SkipIfCpuDialect("Inline PTX is only executable on CUDA devices");

        uint[] data = [0];
        ContractsAndPtxKernels.LaunchInlinePtxKernel(data);
        Assert.Equal(999u, data[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_ConstexprPropagation_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<ContractsAndPtxTarget>(
            nameof(ContractsAndPtxTarget.ConstexprPropagation), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.Contains("32*2", cudaSource);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_WmmaTensorCores_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<ContractsAndPtxTarget>(
            nameof(ContractsAndPtxTarget.WmmaTensorCores), dialect: CurrentDialect);

        if (CurrentDialect.Capabilities.HasFlag(DialectCapabilities.InlineAssembly))
        {
            Assert.Empty(diagnostics);
            Assert.Contains("// wmma.mma.sync.aligned.row.col.m16n16k16.f32.f32", cudaSource);
        }
        else
        {
            diagnostics.AssertContains(GSharpDiagnostics.InlinePtxNotSupportedByDialect);
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


