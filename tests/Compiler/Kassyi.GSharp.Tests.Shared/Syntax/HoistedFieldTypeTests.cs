using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Compiler)]
public class HoistedFieldTypeTests : TranspilerTestBase
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_HoistedVarKernel_CalculatesCorrectly()
    {
        const int totalThreads = 32;
        int[] data = new int[totalThreads];
        for (int i = 0; i < totalThreads; i++)
        {
            data[i] = i + 1;
        }

        HoistedFieldTypeKernels.LaunchHoistedVarKernel(
            data,
            blockDimX: (uint)totalThreads
        );

        for (int i = 0; i < totalThreads; i++)
        {
            Assert.Equal(i + 1, data[i]);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_FloatWarpReductionKernel_CalculatesCorrectly()
    {
        const int threadsPerWarp = 32;
        const int numWarps = 4;
        const int totalThreads = threadsPerWarp * numWarps;

        float[] input = new float[totalThreads];
        for (int i = 0; i < totalThreads; i++)
        {
            input[i] = i + 0.25f;
        }

        float[] output = new float[numWarps];

        HoistedFieldTypeKernels.LaunchFloatWarpReductionKernel(
            input,
            output,
            blockDimX: (uint)totalThreads
        );

        for (int w = 0; w < numWarps; w++)
        {
            float expectedWarpSum = 0f;
            for (int i = 0; i < threadsPerWarp; i++)
            {
                expectedWarpSum += input[w * threadsPerWarp + i];
            }

            Assert.Equal(expectedWarpSum, output[w], precision: 4);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StructVarKernel_CalculatesCorrectly()
    {
        const int totalThreads = 32;
        HoistedPoint[] items = new HoistedPoint[totalThreads];
        for (int i = 0; i < totalThreads; i++)
        {
            items[i] = new HoistedPoint { X = i, Y = i * 2.5f };
        }

        float[] output = new float[totalThreads];

        HoistedFieldTypeKernels.LaunchStructVarKernel(
            items,
            output,
            blockDimX: (uint)totalThreads
        );

        for (int i = 0; i < totalThreads; i++)
        {
            Assert.Equal(items[i].X + items[i].Y, output[i], precision: 4);
        }
    }
}
