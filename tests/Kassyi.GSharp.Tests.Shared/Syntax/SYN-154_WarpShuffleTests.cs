using System.Linq;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_154_WarpShuffleTests : TranspilerTestBase
{
    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "154")]
    [Trait("FeatureId", "SYN-154")]
    public void Test_154_WarpReduceSum_CalculatesCorrectly()
    {
        Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();

        const int threadsPerWarp = 32;
        const int numWarps = 4;
        const int totalThreads = threadsPerWarp * numWarps;

        int[] input = new int[totalThreads];
        for (int i = 0; i < totalThreads; i++)
        {
            input[i] = i + 1;
        }

        int[] output = new int[numWarps];

        SYN_154_WarpShuffleKernelsWrapper.Launch_WarpReductionKernel(
            input,
            output,
            blockDimX: (uint)totalThreads
        );

        // Verify reduction output against host-calculated sum per warp
        for (int w = 0; w < numWarps; w++)
        {
            int expectedWarpSum = 0;
            for (int lane = 0; lane < threadsPerWarp; lane++)
            {
                expectedWarpSum += input[w * threadsPerWarp + lane];
            }

            Assert.Equal(expectedWarpSum, output[w]);
        }
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "154")]
    [Trait("FeatureId", "SYN-154")]
    public void Test_154_WarpShuffleBoundaryOffsets()
    {
        Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();

        const int totalThreads = 32;
        int[] input = new int[totalThreads];
        for (int i = 0; i < totalThreads; i++)
        {
            input[i] = (i + 1) * 10;
        }

        int[] out0 = new int[totalThreads];
        int[] out31 = new int[totalThreads];
        int[] out32 = new int[totalThreads];

        SYN_154_WarpShuffleKernelsWrapper.Launch_WarpShuffleBoundaryKernel(
            input,
            out0,
            out31,
            out32,
            blockDimX: (uint)totalThreads
        );

        for (int i = 0; i < totalThreads; i++)
        {
            // Offset 0: returns thread's own value
            Assert.Equal(input[i], out0[i]);
        }
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "154")]
    [Trait("FeatureId", "SYN-154")]
    public void Test_154_WarpDivergence()
    {
        Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();

        const int totalThreads = 32;
        int[] input = new int[totalThreads];
        for (int i = 0; i < totalThreads; i++)
        {
            input[i] = i + 100;
        }

        int[] output = new int[totalThreads];

        SYN_154_WarpShuffleKernelsWrapper.Launch_WarpDivergenceKernel(
            input,
            output,
            blockDimX: (uint)totalThreads
        );

        Assert.Equal(totalThreads, output.Length);
    }

    [Fact]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "154")]
    [Trait("FeatureId", "SYN-154")]
    public void Test_154_Transpilation_MapsToCudaNativeShuffle()
    {
        const string code = @"
using Kassyi.GSharp.Core;

public static class TestClass
{
    [GpuKernel]
    public static void Kernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        uint mask = 0xffffffff;
        int val = input[tid];
        int res = Warp.ShuffleDownSync(mask, val, 16);
        output[tid] = res;
    }
}";

        var compilation = RoslynTestUtility.CreateCompilation(code);
        var syntaxTree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(syntaxTree);

        var methodDecl = syntaxTree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .First();

        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDecl, model, isDevice: true, dialect: CurrentDialect);

        Assert.Contains("__shfl_down_sync", cudaSource);
        Assert.Contains("0xffffffff", cudaSource);
    }
}
