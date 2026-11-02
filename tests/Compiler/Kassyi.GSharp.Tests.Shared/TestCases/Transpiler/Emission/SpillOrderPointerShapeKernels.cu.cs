using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.Emission;

/// <summary>
/// Managed execution test cases for #958 (pointer locals across barriers and address-of hoisted locals/parameters).
/// </summary>
[SkipSnapshotConsistency("Managed execution test cases for #958 shapes")]
public static unsafe partial class SpillOrderPointerShapeKernels
{
    // Shape 9 (#958 CS0214): External pointer stored in a local pointer variable across Gpu.SyncThreads()
    [GpuKernel]
    public static void PointerLocalAcrossBarrierKernel(nint baseAddress, int[] output)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int* p = (int*)baseAddress + tid;
        *p += 10;
        Gpu.SyncThreads();
        output[tid] = *p;
    }

    public static void RunPointerLocalAcrossBarrier(nint baseAddress, int[] output, uint blockDimX) =>
        LaunchPointerLocalAcrossBarrierKernel(baseAddress, output, blockDimX: blockDimX);

    // Shape 10 (#958 CS0212): Taking address of a value parameter within a state before Gpu.SyncThreads()
    [GpuKernel]
    public static void AddressOfValueParameterBeforeBarrierKernel(int[] output, int value)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int* p = &value;
        *p += tid + 5;
        *(&value) += 3;
        Gpu.SyncThreads();
        output[tid] = value;
    }

    public static void RunAddressOfValueParameterBeforeBarrier(int[] output, int value, uint blockDimX) =>
        LaunchAddressOfValueParameterBeforeBarrierKernel(output, value, blockDimX: blockDimX);

    // Shape 11 (#958 / N2 CS0212): Taking address of a hoisted local variable within a state before Gpu.SyncThreads()
    [GpuKernel]
    public static void AddressOfHoistedLocalBeforeBarrierKernel(int[] output, int seed)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int localVal = seed + tid;
        {
            int* p = &localVal;
            *p += 7;
        }
        Gpu.SyncThreads();
        output[tid] = localVal;
    }

    public static void RunAddressOfHoistedLocalBeforeBarrier(int[] output, int seed, uint blockDimX) =>
        LaunchAddressOfHoistedLocalBeforeBarrierKernel(output, seed, blockDimX: blockDimX);
}
