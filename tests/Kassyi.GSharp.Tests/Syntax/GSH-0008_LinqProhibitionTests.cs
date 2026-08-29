namespace Kassyi.GSharp.Tests.Syntax;

public class GSH_0008_LinqProhibitionTests
{
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GSH-0008")]
    [InlineData(new[] { -5, 0, 3, 10, -2 }, 5)]
    [InlineData(new[] { 1, 2, 3 }, 3)]
    [InlineData(new int[0], 0)]
    public void TestStandardImperativeLoopKernel_HappyPath(int[] input, int length)
    {
        if (length == 0)
        {
            int[] emptyInput = [];
            int[] emptyOutput = [];
            GSH_0008_LinqProhibitionKernelsWrapper.Launch_StandardImperativeLoopKernel(emptyInput, emptyOutput, 0, blockDimX: 1u);
            Assert.Empty(emptyOutput);
            return;
        }

        int[] output = new int[length];
        GSH_0008_LinqProhibitionKernelsWrapper.Launch_StandardImperativeLoopKernel(input, output, length, blockDimX: (uint)length);

        for (int i = 0; i < length; i++)
        {
            int expected = input[i] > 0 ? input[i] * 2 : 0;
            Assert.Equal(expected, output[i]);
        }
    }
}
