using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;


namespace Kassyi.GSharp.Stdlib.GpuMath.Tests;

[Trait("Category", "GpuExecution")]
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

    // Empty requests launch nothing (a launch with an empty grid would fail).
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Orchestrator_EmptyRequests_DoNothing()
    {
        CudaCapability.EnsureGpuAvailable();
        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        using var buffer = new GpuBigUInt(workspace, 4);
        var ptr = buffer.DevicePointer;

        // mutant: StreamCompactionOrchestrator.cs:20-23 early return removed
        var scanError = Record.Exception(() => StreamCompactionOrchestrator.ExecuteExclusiveScan(workspace, ptr, ptr, 0));
        Assert.Null(scanError);

        // mutant: StreamCompactionOrchestrator.cs:89 `||` -> `&&`, `<= 0` -> `< 0` (either operand), L91 `return` removed
        foreach (var (count, words) in new[] { (0, 1), (1, 0), (0, 0) })
        {
            var request = new StreamCompactionOrchestrator.StreamCompactionRequest(ptr, ptr, ptr, ptr, count, words);
            var error = Record.Exception(() => StreamCompactionOrchestrator.CompactArray(workspace, request));
            Assert.Null(error);
        }

        workspace.Provider.SynchronizeStream(workspace.Stream);
    }

    // The output is the exclusive prefix sum of the input: in one block (up to 2048 elements) and in several
    // blocks, whose block sums are scanned recursively and added back (2049, 5000, 6151 elements).
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    // mutant: StreamCompactionOrchestrator.cs:30 `(elementCount + 1) / 2` -> `(elementCount + 1) * 2`
    // (from 512 elements the block exceeds 1024 threads)
    [InlineData(1)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(1000)]
    [InlineData(2048)]
    // mutant: L48 / L51 / L57 / L60 (multi-block path: block size, per-block scan, recursive scan, block-sum add)
    [InlineData(2049)]
    [InlineData(5000)]
    [InlineData(6151)]
    public void ExecuteExclusiveScan_MatchesCpuPrefixSum(int elementCount)
    {
        CudaCapability.EnsureGpuAvailable();
        using var workspace = new GpuWorkspace(4 * elementCount + 64, GpuExecutionEngine.GSharpAot);

        var random = new Random(1234 + elementCount);
        var input = new uint[elementCount];
        var expected = new uint[elementCount];
        uint sum = 0;
        for (var i = 0; i < elementCount; i++)
        {
            input[i] = (uint)random.Next(4);
            expected[i] = sum;
            sum += input[i];
        }

        using var gpuInput = new GpuBigUInt(workspace, input);
        using var gpuOutput = new GpuBigUInt(workspace, elementCount);

        StreamCompactionOrchestrator.ExecuteExclusiveScan(workspace, gpuOutput.DevicePointer, gpuInput.DevicePointer, elementCount);

        Assert.Equal(expected, gpuOutput.ToHostArray());
    }

    // Elements that are not selected leave zeros behind them, even in an output buffer that held garbage
    // (VectorWhereExpression returns the full length and relies on the zero tail).
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void CompactArray_ClearsTailOfDirtyOutput()
    {
        CudaCapability.EnsureGpuAvailable();
        const int ElementCount = 16;
        const int WordsPerElement = 4;
        using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        var input = new uint[ElementCount * WordsPerElement];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = (uint)(i + 1);
        }

        int[] selected = [0, 3, 7, 8, 15];
        var predicates = new uint[ElementCount];
        foreach (var index in selected)
        {
            predicates[index] = 1;
        }

        var dirty = new uint[ElementCount * WordsPerElement];
        Array.Fill(dirty, 0xFFFFFFFFu);

        using var gpuInput = new GpuBigUInt(workspace, input);
        using var gpuPredicates = new GpuBigUInt(workspace, predicates);
        using var gpuScan = new GpuBigUInt(workspace, ElementCount);
        using var gpuOutput = new GpuBigUInt(workspace, dirty);

        StreamCompactionOrchestrator.ExecuteExclusiveScan(workspace, gpuScan.DevicePointer, gpuPredicates.DevicePointer, ElementCount);
        // mutant: StreamCompactionOrchestrator.cs:96 `ElementCount * WordsPerElement` -> `/`, L99 memset removed
        StreamCompactionOrchestrator.CompactArray(
            workspace,
            new StreamCompactionOrchestrator.StreamCompactionRequest(
                gpuOutput.DevicePointer,
                gpuInput.DevicePointer,
                gpuPredicates.DevicePointer,
                gpuScan.DevicePointer,
                ElementCount,
                WordsPerElement));

        var expected = new uint[ElementCount * WordsPerElement];
        for (var k = 0; k < selected.Length; k++)
        {
            Array.Copy(input, selected[k] * WordsPerElement, expected, k * WordsPerElement, WordsPerElement);
        }

        Assert.Equal(expected, gpuOutput.ToHostArray());
    }
}
