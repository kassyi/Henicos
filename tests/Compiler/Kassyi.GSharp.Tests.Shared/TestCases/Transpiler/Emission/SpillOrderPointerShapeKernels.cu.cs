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

    // Shape 12 (#957): Taking address of a value parameter written through pointer before Gpu.SyncThreads()
    [GpuKernel]
    public static void AddressOfValueParameterAcrossBarrierKernel(int[] output, int value)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        {
            int* p = &value;
            *p += tid + 1;
        }
        Gpu.SyncThreads();
        output[tid] = value;
    }

    public static void RunAddressOfValueParameterAcrossBarrier(int[] output, int value, uint blockDimX) =>
        LaunchAddressOfValueParameterAcrossBarrierKernel(output, value, blockDimX: blockDimX);

    // Shape 13 (#966): Address of hoisted local stored in pointer local across Gpu.SyncThreads()
    [GpuKernel]
    public static void AddressOfHoistedLocalAcrossBarrierKernel(int[] output, int seed)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = tid * 10;
        int* p = &x;
        Gpu.SyncThreads();
        *p += 1;
        Gpu.SyncThreads();
        output[tid] = x;
    }

    public static void RunAddressOfHoistedLocalAcrossBarrier(int[] output, int seed, uint blockDimX) =>
        LaunchAddressOfHoistedLocalAcrossBarrierKernel(output, seed, blockDimX: blockDimX);

    // Shape 14 (#970): By-value pointer parameter modified before Gpu.SyncThreads()
    [GpuKernel]
    public static void PointerParameterAcrossBarrierKernel(int* ptr, int[] output)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        ptr = ptr + tid;
        *ptr += tid + 10;
        Gpu.SyncThreads();
        output[tid] = *ptr;
    }

    public static void RunPointerParameterAcrossBarrier(int* ptr, int[] output, uint blockDimX) =>
        LaunchPointerParameterAcrossBarrierKernel(ptr, output, blockDimX: blockDimX);

    // Shape 15 (#1036): Value parameter written through ref local before Gpu.SyncThreads()
    [GpuKernel]
    public static void RefLocalToValueParameterAcrossBarrierKernel(int[] output, int value)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        {
            ref int r = ref value;
            r += tid + 1;
        }
        Gpu.SyncThreads();
        output[tid] = value;
    }

    public static void RunRefLocalToValueParameterAcrossBarrier(int[] output, int value, uint blockDimX) =>
        LaunchRefLocalToValueParameterAcrossBarrierKernel(output, value, blockDimX: blockDimX);
}
