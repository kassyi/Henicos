using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

public class KernelGraphHandlerTests
{
    // GridSize = max(1, ceil(elements / blockSize)): enough blocks to cover every element, and never an empty grid.
    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    // mutant: KernelGraphHandler.cs:69 `elements + blockSize - 1` -> `elements + blockSize + 1`
    // (one block too many at exact multiples: 256 -> 2, 512 -> 3)
    [InlineData(256, 256u, 1u)]
    [InlineData(512, 256u, 2u)]
    [InlineData(1024, 1024u, 1u)]
    // mutant: L70 `gridSize == 0` -> `!= 0`, L71 block removed (an empty input would launch zero blocks)
    [InlineData(0, 256u, 1u)]
    [InlineData(1, 256u, 1u)]
    [InlineData(255, 256u, 1u)]
    [InlineData(257, 256u, 2u)]
    [InlineData(513, 256u, 3u)]
    public void CalculateGridDim_RoundsUpAndNeverReturnsZero(int elements, uint blockSize, uint expectedGrid)
    {
        var (gridSize, actualBlockSize) = KernelGraphHandler.CalculateGridDim(elements, blockSize);

        Assert.Equal(expectedGrid, gridSize);
        Assert.Equal(blockSize, actualBlockSize);
    }
}
