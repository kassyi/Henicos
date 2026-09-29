namespace Kassyi.GSharp.Tests.Transpiler;

public class TranspilerIssuesTests
:  TranspilerTestBase{
    [SkippableTheory]
    [Trait("Category", "Transpiler")]
    [Trait("Category", "GpuExecution")]
    [InlineData(10, 5, 2, 1)]
    [InlineData(10, 2, 5, 2)]
    [InlineData(2, 5, 1, 3)]
    [InlineData(2, 5, 3, 4)]
    public void Test_ComplexConditionalExpression(int x, int y, int z, int expectedResult)
    {
        int[] a = [x];
        int[] b = [y];
        int[] c = [z];
        int[] result = [0];

        Syntax.ControlFlowKernels.LaunchNestedTernaryKernel(a, b, c, result, blockDimX: 1);

        Assert.Equal(expectedResult, result[0]);
    }

    [Fact()]
    [Trait("Category", "Transpiler")]
    public void Test_GeneralTupleSupport()
    {
    }

    [Fact()]
    [Trait("Category", "Transpiler")]
    public void Test_LocalFunctionsSupport()
    {
    }

    [Fact()]
    [Trait("Category", "Transpiler")]
    public void Test_StackAllocFixedArray()
    {
    }

    [Fact()]
    [Trait("Category", "Transpiler")]
    public void Test_GenericsConstraintResolution()
    {
    }
}
