using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class ConcurrencyAndWarpTests : TranspilerTestBase
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestSharedMemoryKernel()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        int[] dataArray = [1, 2, 3, 4];

        ConcurrencyAndWarpKernels.LaunchSharedMemoryKernel(dataArray, 4, blockDimX: 4, sharedMemBytes: 32);

        Assert.Equal(3, dataArray[0]);
        Assert.Equal(6, dataArray[1]);
        Assert.Equal(9, dataArray[2]);
        Assert.Equal(12, dataArray[3]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestSharedMemory_WarpSyncAcross256Threads()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        const int Size = 256;
        int[] dataArray = new int[Size];
        for (int i = 0; i < Size; i++)
        {
            dataArray[i] = i * 3 + 1;
        }

        ConcurrencyAndWarpKernels.LaunchSharedMemoryReverseKernel(
            dataArray,
            Size,
            blockDimX: Size
        );

        Assert.True(dataArray.AsStackEnumerable().Select((val, i) => val == (Size - 1 - i) * 3 + 1).All(ok => ok));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestIntrinsicsKernel()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        int[] dataArray = new int[2];
        dataArray[0] = 0; // atomic sum
        dataArray[1] = 0; // max value

        ConcurrencyAndWarpKernels.LaunchIntrinsicsKernel(dataArray, blockDimX: 10);

        Assert.Equal(45, dataArray[0]); // Sum of 0..9 is 45
        Assert.Equal(100, dataArray[1]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stress)]
    public void TestAtomicAddStress_HighContention()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        int[] globalSum = [0];
        uint threadsPerBlock = 256;
        uint blocksPerGrid = 16;
        int expectedTotalThreads = (int)(threadsPerBlock * blocksPerGrid); // 4096 threads

        ConcurrencyAndWarpKernels.LaunchAtomicAddStressKernel(
            globalSum,
            gridDimX: blocksPerGrid,
            blockDimX: threadsPerBlock
        );

        Assert.Equal(expectedTotalThreads, globalSum[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stress)]
    public void TestAtomicOrMaxStress_HighContention()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        uint[] globalState = [0, 0];
        uint threadsPerBlock = 256;
        uint blocksPerGrid = 16;
        uint expectedTotalThreads = threadsPerBlock * blocksPerGrid; // 4096 threads

        ConcurrencyAndWarpKernels.LaunchAtomicOrMaxStressKernel(
            globalState,
            gridDimX: blocksPerGrid,
            blockDimX: threadsPerBlock
        );

        // First 31 threads will OR their bits: 1 << 0 to 1 << 30
        uint expectedOr = int.MaxValue; // 0x7FFFFFFF
        Assert.Equal(expectedOr, globalState[0]);

        // Max thread ID will be 4095
        Assert.Equal(expectedTotalThreads - 1, globalState[1]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_WarpPrimitives_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod(
            typeof(WarpShuffleKernels), nameof(WarpShuffleKernels.WarpReductionKernel), isDevice: false, dialect: new GSharpCudaDialect());

        Assert.Empty(diagnostics);
        Assert.Contains("__shfl_down_sync", cudaSource);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_CooperativeGroups_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<ConcurrencyAndWarpTarget>(
            nameof(ConcurrencyAndWarpTarget.CooperativeGroups), dialect: new GSharpCudaDialect());

        Assert.Empty(diagnostics);
        Assert.Contains("cg::this_grid().sync();", cudaSource);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_AsyncCopyTma_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<ConcurrencyAndWarpTarget>(
            nameof(ConcurrencyAndWarpTarget.AsyncCopyTma), dialect: new GSharpCudaDialect());

        Assert.Empty(diagnostics);
        Assert.Contains("cp.async.commit_group;", cudaSource);
        Assert.Contains("cp.async.wait_all;", cudaSource);
    }
}

public class ConcurrencyAndWarpTarget
{
    public static void CooperativeGroups(int[] data)
    {
        data[ThreadIndex.X] += 1;
        Gpu.SyncGrid();
        data[ThreadIndex.X] *= 2;
    }

    public static void AsyncCopyTma(uint[] data)
    {
        Gpu.InlinePtx("cp.async.commit_group;");
        Gpu.InlinePtx("cp.async.wait_all;");
        data[0] = 1u;
    }
}


