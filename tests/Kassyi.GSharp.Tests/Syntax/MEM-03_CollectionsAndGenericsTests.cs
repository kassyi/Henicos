using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class MEM_03_CollectionsAndGenericsTests
{


    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-03")]
    [InlineData(new int[] { 10, 20 }, 2, new int[] { 0, 0 })]
    [InlineData(new int[] { 5 }, 1, new int[] { 0 })]
    public void Test_MEM03_GenericConstraints(int[] input, int length, int[] expected)
    {
        MEM_03_CollectionsAndGenericsKernelsWrapper.Launch_GenericConstraintsKernel(input, length, gridDimX: (uint)length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(expected[i], input[i]);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-03")]
    [InlineData(new int[] { 0, 0 }, 2, new int[] { 100, 100 })]
    public void Test_MEM03_GenericFactory(int[] input, int length, int[] expected)
    {
        MEM_03_CollectionsAndGenericsKernelsWrapper.Launch_GenericFactoryKernel(input, length, gridDimX: (uint)length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(expected[i], input[i]);
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
        MEM_03_CollectionsAndGenericsKernelsWrapper.Launch_CollectionIngestionKernel(input, length, gridDimX: (uint)length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(expected[i], input[i]);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-03")]
    [InlineData(new int[] { 5, 10 }, 2, new int[] { 6, 11 })]
    [InlineData(new int[] { 0 }, 1, new int[] { 1 })]
    public void Test_MEM03_ExhaustiveLoopDisposal(int[] input, int length, int[] expected)
    {
        MEM_03_CollectionsAndGenericsKernelsWrapper.Launch_ExhaustiveLoopDisposalKernel(input, length, gridDimX: (uint)length);

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
        MEM_03_CollectionsAndGenericsKernelsWrapper.Launch_ImplicitForeachDisposalKernel(input, length, gridDimX: (uint)length);

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
        MEM_03_CollectionsAndGenericsKernelsWrapper.Launch_DeferredEnumerationKernel(input, length, gridDimX: (uint)length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(expected[i], input[i]);
        }
    }
}
