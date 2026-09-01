namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_17_TargetTypedNewTests
:  TranspilerTestBase{
    [MemberData(nameof(TranspilerTestBase.Dialects))]
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-17")]
    public void Test_SYN17_HappyPath_TargetTypedNew(ITranspilerDialect dialect)
    {
        int[] output = [0];

        SYN_17_TargetTypedNewKernelsWrapper.Launch_HappyPathKernel(output, blockDimX: 1);

        // tid 0: c1.Value = 100, c2.Value = 200 -> output[0] = 300
        Assert.Equal(300, output[0]);
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-17")]
    [InlineData(0, 0)]  // tid 0: p.Id = 0, p.Child.Value = 0 -> 0
    [InlineData(1, 6)]  // tid 1: p.Id = 1, p.Child.Value = 5 -> 6
    [InlineData(2, 12)] // tid 2: p.Id = 2, p.Child.Value = 10 -> 12
    public void Test_SYN17_EdgeCase_NestedTargetTypedNewInInitializer(int tid, int expectedOutput)
    {
        int[] output = new int[3];

        SYN_17_TargetTypedNewKernelsWrapper.Launch_NestedInitializerKernel(output, blockDimX: 3);

        Assert.Equal(expectedOutput, output[tid]);
    }
}
