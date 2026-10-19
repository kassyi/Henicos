namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class DigitSeparatorLiteralTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_DigitSeparatorLiterals_Execution()
    {
        int[] ints = [0];
        uint[] uints = [0];
        long[] longs = [0];
        ulong[] ulongs = [0];
        float[] floats = [0];
        double[] doubles = [0];

        DigitSeparatorLiteralKernels.LaunchDigitSeparatorKernel(ints, uints, longs, ulongs, floats, doubles);

        Assert.Equal(1_000_000, ints[0]);
        Assert.Equal(0xFFFF0000u, uints[0]);
        Assert.Equal(10_000_000_000L, longs[0]);
        Assert.Equal(0b10101010UL, ulongs[0]);
        Assert.Equal(1000f, floats[0]);
        Assert.Equal(10.5e10, doubles[0]);
    }
}
