namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class TargetTypedNewTests
:  TranspilerTestBase{
    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_HappyPath_TargetTypedNew()
    {
        int[] output = [0];

        TargetTypedNewKernels.LaunchHappyPathKernel(output, blockDimX: 1);

        // tid 0: c1.Value = 100, c2.Value = 200 -> output[0] = 300
        Assert.Equal(300, output[0]);
    }

    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [InlineData(0, 0)]  // tid 0: p.Id = 0, p.Child.Value = 0 -> 0
    [InlineData(1, 6)]  // tid 1: p.Id = 1, p.Child.Value = 5 -> 6
    [InlineData(2, 12)] // tid 2: p.Id = 2, p.Child.Value = 10 -> 12
    public void Test_EdgeCase_NestedTargetTypedNewInInitializer(int tid, int expectedOutput)
    {
        int[] output = new int[3];

        TargetTypedNewKernels.LaunchNestedInitializerKernel(output, blockDimX: 3);

        Assert.Equal(expectedOutput, output[tid]);
    }
}
