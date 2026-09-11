namespace Kassyi.GSharp.Tests.Transpiler;

public class GSH_01_05_TranspilerIssuesTests
:  TranspilerTestBase{
    [SkippableTheory]
    [Trait("FeatureId", "GSH-01")]
    [Trait("Category", "Transpiler")]
    [Trait("Category", "GpuExecution")]
    [InlineData(10, 5, 2, 1)]
    [InlineData(10, 2, 5, 2)]
    [InlineData(2, 5, 1, 3)]
    [InlineData(2, 5, 3, 4)]
    public void Test_GSH01_ComplexConditionalExpression(int x, int y, int z, int expectedResult)
    {
        Kassyi.GSharp.Tests.Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        int[] a = [x];
        int[] b = [y];
        int[] c = [z];
        int[] result = [0];

        Syntax.SYN_01_09_ControlFlowKernelsWrapper.Launch_NestedTernaryKernel(a, b, c, result, blockDimX: 1);

        Assert.Equal(expectedResult, result[0]);
    }

    [Fact(Skip="未実装")]
    [Trait("FeatureId", "GSH-02")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH02_GeneralTupleSupport()
    {
    }

    [Fact(Skip="未実装")]
    [Trait("FeatureId", "GSH-03")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH03_LocalFunctionsSupport()
    {
    }

    [Fact(Skip="未実装")]
    [Trait("FeatureId", "GSH-04")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH04_StackAllocFixedArray()
    {
    }

    [Fact(Skip="未実装")]
    [Trait("FeatureId", "GSH-05")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH05_GenericsConstraintResolution()
    {
    }
}
