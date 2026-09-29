using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Runtime;

public class RT02_LaunchDimensionTests
{
    [Theory]
    [Trait("FeatureId", "RT-02")]
    [Trait("Category", "Boundary")]
    [InlineData(1000, 256, 4u, 256u)]
    [InlineData(1024, 1024, 1u, 1024u)]
    [InlineData(1025, 1024, 2u, 1024u)]
    [InlineData(0, 256, 0u, 0u)]
    public void Test_RT02_Calculate1D_ReturnsCorrectDimensions(int total, int tbp, uint expectedGrid, uint expectedBlock)
    {
        var (grid, block) = LaunchDimension.Calculate1D(total, tbp);
        Assert.Equal(expectedGrid, grid);
        Assert.Equal(expectedBlock, block);
    }

    [Theory]
    [Trait("FeatureId", "RT-02")]
    [Trait("Category", "SadPath")]
    [InlineData(-1, 256)]
    [InlineData(1000, 0)]
    [InlineData(1000, -10)]
    [InlineData(1000, 1025)] // > 1024 (hardware limit)
    public void Test_RT02_Calculate1D_InvalidInputs_ThrowsArgumentOutOfRangeException(int total, int tbp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchDimension.Calculate1D(total, tbp));
    }
}
