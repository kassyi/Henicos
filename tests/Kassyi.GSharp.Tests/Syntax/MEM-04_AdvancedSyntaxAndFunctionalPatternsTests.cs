namespace Kassyi.GSharp.Tests.Syntax;

public class MEM_04_AdvancedSyntaxAndFunctionalPatternsTests
{
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    public void Test_MEM04_TupleAndDeconstructionKernel()
    {
        int[] data = [10, 25, 50];
        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_TupleAndDeconstructionKernel(data, blockDimX: 3);
        Assert.Equal(10, data[0]);
        Assert.Equal(25, data[1]);
        Assert.Equal(50, data[2]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    public void Test_MEM04_RecordMoveWithKernel()
    {
        int[] data = [5, 12];
        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_RecordMoveWithKernel(data, blockDimX: 2);
        Assert.Equal(10, data[0]);
        Assert.Equal(24, data[1]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    public void Test_MEM04_DuckTypedUsingKernel()
    {
        int[] data = [3, 7];
        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_DuckTypedUsingKernel(data, blockDimX: 2);
        Assert.Equal(30, data[0]);
        Assert.Equal(70, data[1]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    public void Test_MEM04_BorrowingClosureKernel()
    {
        int[] data = [15, 30];
        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_BorrowingClosureKernel(data, blockDimX: 2);
        Assert.Equal(30, data[0]);
        Assert.Equal(60, data[1]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    public void Test_MEM04_MoveCaptureClosureKernel()
    {
        int[] data = [8, 14];
        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_MoveCaptureClosureKernel(data, blockDimX: 2);
        Assert.Equal(16, data[0]);
        Assert.Equal(28, data[1]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    public void Test_MEM04_RefReassignmentKernel()
    {
        int[] data = [20, 40];
        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_RefReassignmentKernel(data, blockDimX: 2);
        Assert.Equal(120, data[0]);
        Assert.Equal(140, data[1]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    public void Test_MEM04_AwaitUsingKernel()
    {
        int[] data = [10, 20];
        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_AwaitUsingKernel(data, blockDimX: 2);
        Assert.Equal(60, data[0]);
        Assert.Equal(70, data[1]);
    }
}
