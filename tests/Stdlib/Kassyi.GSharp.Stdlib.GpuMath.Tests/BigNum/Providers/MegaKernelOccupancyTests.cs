using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

// Properties checked here (property-test-design §1): a block of the largest size its launcher can use fits on an SM
// for each cooperative mega-kernel (the CUDA driver's occupancy query is the oracle), with the least and the most
// dynamic shared memory the launcher passes. InverseSqrt and Reciprocal choose among 1024, 768, 512 and 256 threads:
// with more than 64 registers per thread 1024 does not fit, and the launcher then takes 768 without a word, so only
// a query fixed to [1024] notices it. ModPow always launches one block of 256 threads.
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class MegaKernelOccupancyTests(ITestOutputHelper output)
{
    private const string InverseSqrtKernelName = "GpuBigIntSqrtKernels.InverseSqrtNewtonRaphsonMegaKernel";
    private const string ReciprocalKernelName = "GpuBigIntDivRemKernels.ReciprocalNewtonRaphsonMegaKernel";
    private const string ModPowKernelName = "GpuBigIntDivRemKernels.ModPowNttMegaKernel";

    /// <summary>Each mega-kernel and the largest block size its launcher uses.</summary>
    public static TheoryData<string, int> MegaKernels() => new()
    {
        { InverseSqrtKernelName, 1024 },
        { ReciprocalKernelName, 1024 },
        { ModPowKernelName, 256 },
    };

    // The failure message of ChooseBlockSize names the registers and the shared memory that do not fit.
    // mutant: GpuBigIntNttKernels.cu.cs TransposeTiles without [MethodImpl(MethodImplOptions.NoInlining)]
    //         (the transposes inlined into the kernels)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(MegaKernels))]
    public void ChooseBlockSize_LargestBlockOfMegaKernel_FitsOnAnSm(string kernelName, int blockSize)
    {
        CudaCapability.EnsureGpuAvailable();
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var scope = new GpuKernelLoader.CudaContextScope(workspace.IsolatedContext.Handle);
        var hfunc = Resolve(kernelName);
        var attributes = GpuKernelLoader.GetKernelAttributes(hfunc);
        output.WriteLine(
            $"{kernelName}: {attributes.NumRegs} registers/thread, " +
            $"{attributes.SharedSizeBytes} B static shared memory, {attributes.LocalSizeBytes} B local memory");
        // Static shared memory is 12672 B (3 * 32 * 33 words in TransposeTileBatches) + 400 B of block-scan buffers;
        // 4 tiles (17296 B) + 34048 B dynamic at 2^25 points passes 48 KB, and a non-constant SharedMemory size drops
        // static shared memory to 400 B.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntFermatKernels.cu.cs:149
        //         `private const int MaxTilesPerBatch = 3;` -> `4`
        // input: any of the three mega-kernels
        Assert.Equal(13072, attributes.SharedSizeBytes);

        foreach (var transformLength in new[] { 512, 1 << 25 })
        {
            var sharedMemBytes = CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(transformLength);
            var chosen = CooperativeLaunchOccupancy.ChooseBlockSize(
                hfunc, workspace.DeviceOrdinal, sharedMemBytes, kernelName, [blockSize]);
            Assert.Equal(blockSize, chosen);
        }
    }

    // The query can fail: 1 MiB of dynamic shared memory fits no SM, so the same call throws.
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(MegaKernels))]
    public void ChooseBlockSize_OneMebibyteOfSharedMemory_Throws(string kernelName, int blockSize)
    {
        CudaCapability.EnsureGpuAvailable();
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var scope = new GpuKernelLoader.CudaContextScope(workspace.IsolatedContext.Handle);
        var hfunc = Resolve(kernelName);

        var exception = Assert.Throws<InvalidOperationException>(() => CooperativeLaunchOccupancy.ChooseBlockSize(
            hfunc, workspace.DeviceOrdinal, 1u << 20, kernelName, [blockSize]));
        output.WriteLine(exception.Message);
    }

    private static IntPtr Resolve(string kernelName) => kernelName switch
    {
        InverseSqrtKernelName => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
        ReciprocalKernelName => GpuBigIntDivRemKernels.ResolveReciprocalNewtonRaphsonMegaKernel(),
        ModPowKernelName => GpuBigIntDivRemKernels.ResolveModPowNttMegaKernel(),
        _ => throw new ArgumentOutOfRangeException(nameof(kernelName), kernelName, "Not a cooperative mega-kernel."),
    };
}
