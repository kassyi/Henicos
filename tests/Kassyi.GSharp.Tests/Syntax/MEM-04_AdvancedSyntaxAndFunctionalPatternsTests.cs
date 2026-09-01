using System;

namespace Kassyi.GSharp.Tests.Syntax;

public class MEM_04_AdvancedSyntaxAndFunctionalPatternsTests
{
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    [Trait("FeatureId", "GSH-02")]
    [InlineData(new int[0])]
    [InlineData(new int[] { 0 })]
    [InlineData(new int[] { -1 })]
    [InlineData(new int[] { int.MinValue, int.MaxValue })]
    [InlineData(new int[] { 5, 12 })]
    public void Test_MEM04_TupleAndDeconstructionKernel(int[] data)
    {
        if (data.Length == 0)
        {
            MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_TupleAndDeconstructionKernel(data, blockDimX: 0U);
            return;
        }

        int[] expected = new int[data.Length];
        Array.Copy(data, expected, data.Length);

        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_TupleAndDeconstructionKernel(data, blockDimX: (uint)data.Length);

        for (int i = 0; i < data.Length; i++)
        {
            Assert.Equal(expected[i], data[i]);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GSH-02")]
    [InlineData(new int[0])]
    [InlineData(new int[] { 0 })]
    [InlineData(new int[] { -1 })]
    [InlineData(new int[] { int.MinValue / 2, int.MaxValue / 2 })]
    [InlineData(new int[] { 7, 15 })]
    public void Test_GSH02_TupleReturnAndDeconstructionKernel(int[] data)
    {
        if (data.Length == 0)
        {
            MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_TupleReturnAndDeconstructionKernel(data, blockDimX: 0U);
            return;
        }

        int[] expected = new int[data.Length];
        Array.Copy(data, expected, data.Length);

        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_TupleReturnAndDeconstructionKernel(data, blockDimX: (uint)data.Length);

        for (int i = 0; i < data.Length; i++)
        {
            Assert.Equal(expected[i] + 10, data[i]);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GSH-02")]
    [InlineData(new int[0])]
    [InlineData(new int[] { 0 })]
    [InlineData(new int[] { -1 })]
    [InlineData(new int[] { int.MinValue / 10, int.MaxValue / 10 })]
    [InlineData(new int[] { 3, 8 })]
    public void Test_GSH02_ReassignmentDeconstructionKernel(int[] data)
    {
        if (data.Length == 0)
        {
            MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_ReassignmentDeconstructionKernel(data, blockDimX: 0U);
            return;
        }

        int[] expected = new int[data.Length];
        Array.Copy(data, expected, data.Length);

        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_ReassignmentDeconstructionKernel(data, blockDimX: (uint)data.Length);

        for (int i = 0; i < data.Length; i++)
        {
            Assert.Equal(expected[i] * 5, data[i]);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GSH-02")]
    [InlineData(new int[0])]
    [InlineData(new int[] { 0 })]
    [InlineData(new int[] { -1 })]
    [InlineData(new int[] { int.MinValue / 4, int.MaxValue / 4 })]
    [InlineData(new int[] { 2, 9 })]
    public void Test_GSH02_NestedTupleDeconstructionKernel(int[] data)
    {
        if (data.Length == 0)
        {
            MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_NestedTupleDeconstructionKernel(data, blockDimX: 0U);
            return;
        }

        int[] expected = new int[data.Length];
        Array.Copy(data, expected, data.Length);

        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_NestedTupleDeconstructionKernel(data, blockDimX: (uint)data.Length);

        for (int i = 0; i < data.Length; i++)
        {
            // Formula in kernel: a + b + c = data[tid] + (data[tid] + 1) + (data[tid] + 2) = 3 * data[tid] + 3
            Assert.Equal(3 * expected[i] + 3, data[i]);
        }
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    public void Test_MEM04_OutDiscardKernel()
    {
        int[] data = { 5 };
        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_OutDiscardKernel(data, blockDimX: 1U);
        Assert.Equal(10, data[0]);
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    [InlineData(new int[0])]
    [InlineData(new int[] { 0 })]
    [InlineData(new int[] { -1 })]
    [InlineData(new int[] { int.MinValue / 2, int.MaxValue / 2 })]
    [InlineData(new int[] { 5, 12 })]
    public void Test_MEM04_RecordMoveWithKernel(int[] data)
    {
        if (data.Length == 0)
        {
            MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_RecordMoveWithKernel(data, blockDimX: 0U);
            return;
        }

        int[] expected = new int[data.Length];
        Array.Copy(data, expected, data.Length);

        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_RecordMoveWithKernel(data, blockDimX: (uint)data.Length);
        
        for (int i = 0; i < data.Length; i++)
        {
            Assert.Equal(expected[i] * 2, data[i]);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    [InlineData(new int[0])]
    [InlineData(new int[] { 0 })]
    [InlineData(new int[] { -1 })]
    [InlineData(new int[] { int.MinValue / 10, int.MaxValue / 10 })]
    [InlineData(new int[] { 3, 7 })]
    public void Test_MEM04_DuckTypedUsingKernel(int[] data)
    {
        if (data.Length == 0)
        {
            MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_DuckTypedUsingKernel(data, blockDimX: 0U);
            return;
        }

        int[] expected = new int[data.Length];
        Array.Copy(data, expected, data.Length);

        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_DuckTypedUsingKernel(data, blockDimX: (uint)data.Length);
        
        for (int i = 0; i < data.Length; i++)
        {
            Assert.Equal(expected[i] * 10, data[i]);
        }
    }

    [Fact(Skip = "未実装: 委譲/クロージャ (System.Action) サポート不足")]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    public void Test_MEM04_BorrowingClosureKernel()
    {
        /*
        int[] data = { 5, 12 };
        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_BorrowingClosureKernel(data, blockDimX: (uint)data.Length);
        */
    }

    [Fact(Skip = "未実装: 委譲/クロージャ (System.Action) サポート不足")]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    public void Test_MEM04_MoveCaptureClosureKernel()
    {
        /*
        int[] data = { 5, 12 };
        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_MoveCaptureClosureKernel(data, blockDimX: (uint)data.Length);
        */
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    [InlineData(new int[0])]
    [InlineData(new int[] { 0 })]
    [InlineData(new int[] { -1 })]
    [InlineData(new int[] { int.MinValue, int.MaxValue - 100 })]
    [InlineData(new int[] { 20, 40 })]
    public void Test_MEM04_RefReassignmentKernel(int[] data)
    {
        if (data.Length == 0)
        {
            MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_RefReassignmentKernel(data, blockDimX: 0U);
            return;
        }

        int[] expected = new int[data.Length];
        Array.Copy(data, expected, data.Length);

        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_RefReassignmentKernel(data, blockDimX: (uint)data.Length);
        
        for (int i = 0; i < data.Length; i++)
        {
            Assert.Equal(expected[i] + 100, data[i]);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-04")]
    [InlineData(new int[0])]
    [InlineData(new int[] { 0 })]
    [InlineData(new int[] { -1 })]
    [InlineData(new int[] { int.MinValue, int.MaxValue - 50 })]
    [InlineData(new int[] { 10, 20 })]
    public void Test_MEM04_AwaitUsingKernel(int[] data)
    {
        if (data.Length == 0)
        {
            MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_AwaitUsingKernel(data, blockDimX: 0U);
            return;
        }

        int[] expected = new int[data.Length];
        Array.Copy(data, expected, data.Length);

        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernelsWrapper.Launch_AwaitUsingKernel(data, blockDimX: (uint)data.Length);
        
        for (int i = 0; i < data.Length; i++)
        {
            Assert.Equal(expected[i] + 50, data[i]);
        }
    }
}
