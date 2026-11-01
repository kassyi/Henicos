using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential test kernels for #1000 (ref conditional expressions targeting value parameters across barriers),
/// #944 ([GpuOperatorMacro] mutating an implicit ref value parameter across barriers), and F14 (unsafe blocks
/// containing barriers/shuffles).
/// </summary>
[SkipSnapshotConsistency("Differential test cases for #1000, #944, and #991")]
public static unsafe partial class SpillOrderMutatingGetterKernels
{
    [GpuDeviceFunction]
    private static void MutateByRef(ref int target, int delta)
    {
        target += delta;
    }

    [GpuDeviceFunction]
    private static int ConditionalRefWriteDevice(int left, int right, int selector, int delta)
    {
        (selector == 0 ? ref left : ref right) += delta;
        Gpu.SyncThreads();
        return left * 100 + right;
    }

    [GpuDeviceFunction]
    private static uint OperatorMacroAcrossBarrierDevice(
        OperatorMacroContextState bigCtx,
        uint[] left,
        uint[] right,
        uint[] result)
    {
        OperatorMacroContextValue a = left;
        OperatorMacroContextValue b = right;
        OperatorMacroContextValue c = result;

        c = a + b;
        Gpu.SyncThreads();
        return bigCtx.Calls;
    }

    // Shape 6 (#1000): Conditional ref assignment (c ? ref a : ref b) = ... on kernel value parameters across barrier
    [GpuKernel]
    public static void RefConditionalWriteOnKernelValueParameterAcrossBarrierKernel(
        int[] output,
        int leftParam,
        int rightParam)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        bool pickLeft = (tid & 1) == 0;
        (pickLeft ? ref leftParam : ref rightParam) += tid + 5;
        Gpu.SyncThreads();
        output[tid] = leftParam * 100 + rightParam;
    }

    // Shape 7 (#1000): Conditional ref passed as ref argument Mutate(ref (c ? ref a : ref b)) on kernel value parameters across barrier
    [GpuKernel]
    public static void RefConditionalArgumentOnKernelValueParameterAcrossBarrierKernel(
        int[] output,
        int leftParam,
        int rightParam)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        bool pickLeft = (tid & 1) == 0;
        MutateByRef(ref (pickLeft ? ref leftParam : ref rightParam), tid + 7);
        Gpu.SyncThreads();
        output[tid] = leftParam * 100 + rightParam;
    }

    // Shape 8 (#1000): Conditional ref write on suspending device function value parameters across barrier
    [GpuKernel]
    public static void RefConditionalWriteOnDeviceValueParameterAcrossBarrierKernel(
        int[] output,
        int leftParam,
        int rightParam)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int selector = tid & 1;
        output[tid] = ConditionalRefWriteDevice(leftParam, rightParam, selector, tid + 3);
    }

    // #944: [GpuOperatorMacro] implicitly passing kernel value parameter by ref before Gpu.SyncThreads()
    [GpuKernel]
    public static void OperatorMacroOnKernelValueParameterAcrossBarrierKernel(
        OperatorMacroContextState bigCtx,
        uint[] left,
        uint[] right,
        uint[] result,
        uint[] calls)
    {
        OperatorMacroContextValue a = left;
        OperatorMacroContextValue b = right;
        OperatorMacroContextValue c = result;

        c = a + b;
        Gpu.SyncThreads();
        calls[0] = bigCtx.Calls;
    }

    // #944: [GpuOperatorMacro] implicitly passing suspending device function value parameter by ref before Gpu.SyncThreads()
    [GpuKernel]
    public static void OperatorMacroOnDeviceValueParameterAcrossBarrierKernel(
        OperatorMacroContextState bigCtx,
        uint[] left,
        uint[] right,
        uint[] result,
        uint[] calls)
    {
        calls[0] = OperatorMacroAcrossBarrierDevice(bigCtx, left, right, result);
    }

    // F14: unsafe { ... Gpu.SyncThreads(); ... } flattened by BranchRewriter
    [GpuKernel]
    public static void UnsafeBlockWithSyncThreadsKernel(int[] output)
    {
        var shared = new SharedMemory<int>(32);
        int tid = ThreadIndex.X;
        int peer = (tid + 1) & 31;
        shared[tid] = 0;
        Gpu.SyncThreads();

        unsafe
        {
            shared[tid] = tid + 10;
            Gpu.SyncThreads();
        }

        output[tid] = shared[peer];
    }

    // F14: unsafe { ... Warp.ShuffleSync(...); ... } flattened by BranchRewriter
    [GpuKernel]
    public static void UnsafeBlockWithWarpShuffleKernel(int[] output, int seed)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int lane0Value = 0;

        unsafe
        {
            int local = seed + tid * 3;
            lane0Value = Warp.ShuffleSync(0xFFFFFFFFu, local, 0);
        }

        output[tid] = lane0Value + tid;
    }
}
