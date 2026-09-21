namespace Kassyi.GSharp.Tests.Syntax;

public class GSH_03_LocalFunctionLoweringTests : TranspilerTestBase
{
    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "GSH-03")]
    public void Test_GSH03_LocalFunctionVoid_Execution()
    {
        int[] data = [10, 20, 30];
        GSH_03_LocalFunctionLoweringKernelsWrapper.LaunchLocalFunctionVoidKernel(data, blockDimX: 3);
        Assert.Equal(20, data[0]);
        Assert.Equal(40, data[1]);
        Assert.Equal(60, data[2]);
    }

    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "GSH-03")]
    [InlineData(0, 5, 5)]
    [InlineData(1, 10, 13)]
    [InlineData(10, 5, 35)]
    [InlineData(-1, 0, -3)]
    [InlineData(int.MaxValue / 4, 100, (int.MaxValue / 4) * 3 + 100)]
    [InlineData(int.MinValue, 0, unchecked(int.MinValue * 3))]
    [InlineData(int.MaxValue, 0, unchecked(int.MaxValue * 3))]
    [InlineData(0x55555555, 1, unchecked(0x55555555 * 3 + 1))]
    public void Test_GSH03_LocalFunctionValueReturn_NumericBoundaries_Execution(int input, int bonus, int expected)
    {
        int[] data = [input];
        GSH_03_LocalFunctionLoweringKernelsWrapper.LaunchLocalFunctionValueReturnKernel(data, bonus, blockDimX: 1);
        Assert.Equal(expected, data[0]);
    }

    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "GSH-03")]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 25)]
    [InlineData(-4, 16)]
    public void Test_GSH03_LocalFunctionExpressionBody_Execution(int input, int expected)
    {
        int[] data = [input];
        GSH_03_LocalFunctionLoweringKernelsWrapper.LaunchLocalFunctionExpressionBodyKernel(data, blockDimX: 1);
        Assert.Equal(expected, data[0]);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "GSH-03")]
    public void Test_GSH03_NestedLocalFunction_Execution()
    {
        int[] data = [0, 5, 10];
        GSH_03_LocalFunctionLoweringKernelsWrapper.LaunchNestedLocalFunctionKernel(data, blockDimX: 3);
        Assert.Equal(10, data[0]); // 0 * 2 + 10
        Assert.Equal(20, data[1]); // 5 * 2 + 10
        Assert.Equal(30, data[2]); // 10 * 2 + 10
    }

    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "GSH-03")]
    [InlineData(10, 20, 30)]  // Within [0, 100]
    [InlineData(-50, 10, 0)]  // Below min limit 0 -> clamped to 0
    [InlineData(80, 50, 100)] // Exceeds max limit 100 -> clamped to 100
    [InlineData(-1, 0, 0)]    // Adjacent to lower bound (sum = -1 -> clamped to 0)
    [InlineData(0, 0, 0)]     // Lower boundary (sum = 0 -> 0)
    [InlineData(1, 0, 1)]     // Adjacent inside lower bound (sum = 1 -> 1)
    [InlineData(99, 0, 99)]   // Adjacent inside upper bound (sum = 99 -> 99)
    [InlineData(100, 0, 100)] // Upper boundary (sum = 100 -> 100)
    [InlineData(101, 0, 100)] // Adjacent beyond upper bound (sum = 101 -> clamped to 100)
    public void Test_GSH03_MultiParamLocalFunction_Execution(int aVal, int bVal, int expected)
    {
        int[] a = [aVal];
        int[] b = [bVal];
        int[] result = [0];
        GSH_03_LocalFunctionLoweringKernelsWrapper.LaunchMultiParamLocalFunctionKernel(a, b, result, blockDimX: 1);
        Assert.Equal(expected, result[0]);
    }
}
