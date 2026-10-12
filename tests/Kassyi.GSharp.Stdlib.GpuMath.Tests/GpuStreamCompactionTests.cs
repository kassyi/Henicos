
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests;

public class GpuStreamCompactionTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;

    public GpuStreamCompactionTests()
    {
        _scope = new GpuKernelLoader.CudaContextScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Fact]
    public void BlellochScan_SingleBlock_CorrectlyComputesExclusivePrefixSum()
    {
        // 1. Arrange
        int elementCount = 8;
        uint[] cpuInput = new uint[] { 3, 1, 7, 0, 4, 1, 6, 3 };
        uint[] expectedCpuOutput = new uint[] { 0, 3, 4, 11, 11, 15, 16, 22 };

        uint[] actualOutput = new uint[elementCount];

        // 2. Act
        // We use (elementCount / 2) threads because each thread processes 2 elements
        GpuStreamCompactionKernels.LaunchBlockExclusiveScan(
            actualOutput, cpuInput, elementCount, new uint[1],
            gridDimX: 1, blockDimX: (uint)(elementCount / 2));
        
        // 3. Assert

        for (int i = 0; i < elementCount; i++)
        {
            Assert.Equal(expectedCpuOutput[i], actualOutput[i]);
        }
    }

    [Fact]
    public void BlellochScan_LargePowerOfTwo_CorrectlyComputesExclusivePrefixSum()
    {
        // 1. Arrange
        int elementCount = 1024;
        uint[] cpuInput = new uint[elementCount];
        uint[] expectedCpuOutput = new uint[elementCount];
        
        uint currentSum = 0;
        for (int i = 0; i < elementCount; i++)
        {
            cpuInput[i] = 1; // Input is all 1s
            expectedCpuOutput[i] = currentSum;
            currentSum += cpuInput[i];
        }

        uint[] actualOutput = new uint[elementCount];

        // 2. Act
        GpuStreamCompactionKernels.LaunchBlockExclusiveScan(
            actualOutput, cpuInput, elementCount, new uint[1],
            gridDimX: 1, blockDimX: (uint)(elementCount / 2));
        
        // 3. Assert

        for (int i = 0; i < elementCount; i++)
        {
            Assert.Equal(expectedCpuOutput[i], actualOutput[i]);
        }
    }

    [Fact]
    public void FullStreamCompaction_CorrectlyFiltersAndCompactsArray()
    {
        // 1. Arrange
        int elementCount = 1024;
        uint[] cpuInput = new uint[elementCount];
        uint[] cpuPredicates = new uint[elementCount];
        
        for (int i = 0; i < elementCount; i++)
        {
            cpuInput[i] = (uint)(i * 10);
            cpuPredicates[i] = (uint)(i % 3 == 0 ? 1 : 0); // Keep every 3rd element
        }

        // Expected output
        var expectedOutput = cpuInput.Where((val, index) => cpuPredicates[index] == 1).ToArray();

        uint[] scanResult = new uint[elementCount];
        uint[] blockSums = new uint[1];

        // 2. Act: Prefix Sum
        GpuStreamCompactionKernels.LaunchBlockExclusiveScan(
            scanResult, cpuPredicates, elementCount, blockSums,
            gridDimX: 1, blockDimX: (uint)(elementCount / 2));

        uint[] actualOutput = new uint[expectedOutput.Length];
        
        // 3. Act: Scatter (Compact)
        GpuStreamCompactionKernels.LaunchCompactBigUIntArray(
            actualOutput, cpuInput, cpuPredicates, scanResult, elementCount, 1,
            gridDimX: 1, blockDimX: (uint)elementCount); // 1 thread per element for scatter

        // 4. Assert
        Assert.Equal(expectedOutput.Length, actualOutput.Length);
        for (int i = 0; i < expectedOutput.Length; i++)
        {
            Assert.Equal(expectedOutput[i], actualOutput[i]);
        }
        
        // The total number of valid elements is the last scan result + last predicate
        uint totalElements = scanResult[elementCount - 1] + cpuPredicates[elementCount - 1];
        Assert.Equal((uint)expectedOutput.Length, totalElements);
    }
}
