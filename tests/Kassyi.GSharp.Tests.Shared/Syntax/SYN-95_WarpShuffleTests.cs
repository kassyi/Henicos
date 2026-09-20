using System.Linq;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_95_WarpShuffleTests : TranspilerTestBase
{
    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "154")]
    [Trait("FeatureId", "SYN-95")]
    [InlineData(0xFFFFFFFF, 1, 32)] // Full warp, delta=1
    [InlineData(0xFFFFFFFF, 31, 32)] // Full warp, delta=31
    [InlineData(0xFFFFFFFF, 32, 32)] // Full warp, delta=out-of-bounds
    [InlineData(0x0000FFFF, 1, 16)] // Half warp, delta=1
    [InlineData(0x00000000, 1, 32)] // Zero mask
    [InlineData(0xFFFFFFFF, 0, 32)] // Zero delta
    public void Test_95_WarpShuffleExtrema_CalculatesCorrectly(uint mask, uint delta, int width)
    {
        Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();

        const int totalThreads = 32;
        int[] input = new int[totalThreads];
        for (int i = 0; i < totalThreads; i++)
        {
            input[i] = i + 1;
        }

        int[] output = new int[totalThreads];

        SYN_95_WarpShuffleKernelsWrapper.LaunchWarpShuffleExtremaKernel(
            mask,
            input,
            delta,
            width,
            output,
            blockDimX: (uint)totalThreads
        );

        for (int i = 0; i < totalThreads; i++)
        {
            int logicalLane = i % width;
            int srcLane = logicalLane + (int)delta;
            int expected = input[i];

            if (srcLane < width)
            {
                int globalSrcLane = i - logicalLane + srcLane;
                if (globalSrcLane < totalThreads)
                {
                    expected = input[globalSrcLane];
                }
            }

            Assert.Equal(expected, output[i]);
        }
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "154")]
    [Trait("FeatureId", "SYN-95")]
    public void Test_95_WarpReduceSum_CalculatesCorrectly()
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

        SYN_95_WarpShuffleKernelsWrapper.LaunchWarpReductionKernel(
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
    [Trait("FeatureId", "SYN-95")]
    public void Test_95_WarpShuffleBoundaryOffsets()
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

        SYN_95_WarpShuffleKernelsWrapper.LaunchWarpShuffleBoundaryKernel(
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

            // Offset 31: thread 0 receives thread 31's value, others retain their own (width=32)
            int expected31 = (i == 0) ? input[31] : input[i];
            Assert.Equal(expected31, out31[i]);

            // Offset 32: out of bounds, all retain their own
            Assert.Equal(input[i], out32[i]);
        }
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "154")]
    [Trait("FeatureId", "SYN-95")]
    public void Test_95_WarpDivergence()
    {
        Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();

        const int totalThreads = 32;
        int[] input = new int[totalThreads];
        for (int i = 0; i < totalThreads; i++)
        {
            input[i] = i + 100;
        }

        int[] output = new int[totalThreads];

        SYN_95_WarpShuffleKernelsWrapper.LaunchWarpDivergenceKernel(
            input,
            output,
            blockDimX: (uint)totalThreads
        );

        // lane < 16 branch: delta = 1, width = 16
        // lane 15: out of bounds for width 16 (15 + 1 >= 16), keeps its own value
        Assert.Equal(input[15], output[15]);
        // lane 14: reads 15
        Assert.Equal(input[15], output[14]);

        // lane >= 16 branch: delta = 2, width = 16
        // lane 16: reads 16 + 2 = 18
        Assert.Equal(input[18], output[16]);
        // lane 17: reads 17 + 2 = 19
        Assert.Equal(input[19], output[17]);
        // lane 30: 30 % 16 + 2 = 16 >= 16, keeps its own value
        Assert.Equal(input[30], output[30]);
    }

    [Fact]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "154")]
    [Trait("FeatureId", "SYN-95")]
    public void Test_95_Transpilation_MapsToCudaNativeShuffle()
    {
        const string code = """
            using Kassyi.GSharp.Core ;
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
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(code);
        var syntaxTree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(syntaxTree);

        var methodDecl = syntaxTree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .First();

        var cudaSource =
            MethodTranspiler.GenerateCudaSource(methodDecl, model, isDevice: true, dialect: CurrentDialect);

        Assert.Contains("__shfl_down_sync", cudaSource);
        Assert.Contains("0xffffffff", cudaSource);
    }
}
