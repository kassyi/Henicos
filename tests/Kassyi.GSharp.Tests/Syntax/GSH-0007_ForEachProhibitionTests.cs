namespace Kassyi.GSharp.Tests.Syntax;

public class GSH_0007_ForEachProhibitionTests
{
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GSH-0007")]
    [InlineData(new[] { 1, 2, 3, 4 }, 4)]
    [InlineData(new[] { 10 }, 1)]
    [InlineData(new[] { -1, 0, 1 }, 3)]
    [InlineData(new[] { -10, -20, 30 }, 3)]
    [InlineData(new[] { 0x55555555, 0x12345678 }, 2)]
    [InlineData(new int[0], 0)]
    public void Test_GSH0007_StandardForLoopKernel_HappyPath(int[] input, int length)
    {
        if (length == 0)
        {
            int[] emptyInput = [];
            int[] emptyOutput = [];
            GSH_0007_ForEachProhibitionKernelsWrapper.Launch_StandardForLoopKernel(emptyInput, emptyOutput, 0, blockDimX: 1u);
            Assert.Empty(emptyOutput);
            return;
        }

        int[] output = new int[length];
        int expectedSum = 0;
        for (int i = 0; i < length; i++)
        {
            expectedSum += input[i];
        }

        GSH_0007_ForEachProhibitionKernelsWrapper.Launch_StandardForLoopKernel(input, output, length, blockDimX: (uint)length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(expectedSum + input[i], output[i]);
        }
    }
}
