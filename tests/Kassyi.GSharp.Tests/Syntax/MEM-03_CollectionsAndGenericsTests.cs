using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class MEM_03_CollectionsAndGenericsTests
{
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-03")]
    [InlineData(new int[] { 10, 20, 30 }, 3)]
    [InlineData(new int[] { 0 }, 1)]
    [InlineData(new int[] { -5, 100 }, 2)]
    public void Test_MEM03_GenericConstraints_Monomorphization(int[] data, int length)
    {
        MEM_03_CollectionsAndGenericsKernelsWrapper.Launch_GenericConstraintsKernel(data, length, blockDimX: (uint)length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(0, data[i]);
        }
    }

    [Theory(Skip = "未実装: MEM-03 (Generic Factory のカーネル内展開)")]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-03")]
    [InlineData(new int[] { 0, 0, 0 }, 3)]
    [InlineData(new int[] { 0 }, 1)]
    public void Test_MEM03_GenericFactory_WithOwnership(int[] data, int length)
    {
        MEM_03_CollectionsAndGenericsKernelsWrapper.Launch_GenericFactoryKernel(data, length, blockDimX: (uint)length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(100, data[i]);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-03")]
    [InlineData(new int[] { 1, 2, 3 }, 3, new int[] { 2, 4, 6 })]
    [InlineData(new int[] { 0 }, 1, new int[] { 0 })]
    [InlineData(new int[] { -10, 50 }, 2, new int[] { -20, 100 })]
    public void Test_MEM03_CollectionIngestion(int[] input, int length, int[] expected)
    {
        MEM_03_CollectionsAndGenericsKernelsWrapper.Launch_CollectionIngestionKernel(input, length, blockDimX: (uint)length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(expected[i], input[i]);
        }
    }

    [Theory(Skip = "未実装: MEM-03 (ループ内での exhaustive disposal)")]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-03")]
    [InlineData(new int[] { 5, 10 }, 2, new int[] { 6, 11 })]
    [InlineData(new int[] { 0 }, 1, new int[] { 1 })]
    public void Test_MEM03_ExhaustiveLoopDisposal(int[] input, int length, int[] expected)
    {
        MEM_03_CollectionsAndGenericsKernelsWrapper.Launch_ExhaustiveLoopDisposalKernel(input, length, blockDimX: (uint)length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(expected[i], input[i]);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-03")]
    [InlineData(new int[] { 1, 2, 3, 4 }, 4, new int[] { 6, 7, 8, 9 })]
    [InlineData(new int[] { 42 }, 1, new int[] { 47 })]
    public void Test_MEM03_ImplicitForeachDisposal(int[] input, int length, int[] expected)
    {
        MEM_03_CollectionsAndGenericsKernelsWrapper.Launch_ImplicitForeachDisposalKernel(input, length, blockDimX: (uint)length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(expected[i], input[i]);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-03")]
    [InlineData(new int[] { 5, 15 }, 2, new int[] { 15, 25 })]
    [InlineData(new int[] { -10 }, 1, new int[] { 0 })]
    public void Test_MEM03_DeferredEnumeration(int[] input, int length, int[] expected)
    {
        MEM_03_CollectionsAndGenericsKernelsWrapper.Launch_DeferredEnumerationKernel(input, length, blockDimX: (uint)length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(expected[i], input[i]);
        }
    }
    [Theory]
    [Trait("FeatureId", "MEM-03")]
    [InlineData(new int[] { 1, 2, 3 }, 3, new int[] { 16, 17, 18 })]
    [InlineData(new int[] { 5, 15 }, 2, new int[] { 20, 30 })]
    [InlineData(new int[] { -10 }, 1, new int[] { 5 })]
    public void Test_MEM03_NestedForeachDeferredEnumeration(int[] input, int length, int[] expected)
    {
        MEM_03_CollectionsAndGenericsKernelsWrapper.Launch_NestedDeferredEnumerationKernel(input, length, blockDimX: (uint)length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(expected[i], input[i]);
        }
    }
}
