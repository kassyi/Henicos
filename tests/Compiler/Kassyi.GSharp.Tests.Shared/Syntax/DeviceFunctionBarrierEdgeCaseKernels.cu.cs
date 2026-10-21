// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Edge cases of device functions that suspend: shared memory owned by callee frames, suspending calls whose evaluation
/// is conditional or repeated, expression bodies and by-reference parameters.
/// </summary>
public static partial class DeviceFunctionBarrierEdgeCaseKernels
{
    private const uint FullMask = 0xFFFFFFFFu;

    // --- Shared memory declared in callees ---

    [GpuDeviceFunction]
    public static int DoubleThroughSharedMemory(int value)
    {
        int tid = ThreadIndex.X;
        var inner = new SharedMemory<int>(256);
        inner[tid] = value * 2;
        Gpu.SyncThreads();
        return inner[tid];
    }

    // No barrier: the callee does not suspend, but its buffer must still be its own
    [GpuDeviceFunction]
    public static int IncrementThroughScratch(int value)
    {
        int tid = ThreadIndex.X;
        var scratch = new SharedMemory<int>(256);
        scratch[tid] = value + 1;
        return scratch[tid];
    }

    // Returns what the previous call stored: the buffer belongs to the declaration, not to the call site, as on CUDA
    [GpuDeviceFunction]
    public static int SwapThroughDeclaration(int value)
    {
        int tid = ThreadIndex.X;
        var box = new SharedMemory<int>(256);
        int previous = box[tid];
        Gpu.SyncThreads();
        box[tid] = value;
        Gpu.SyncThreads();
        return previous;
    }

    [GpuKernel]
    public static void CallerAndSuspendingCalleeSharedMemoryKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var outer = new SharedMemory<int>(256);
        outer[tid] = tid + 100;
        Gpu.SyncThreads();

        int helperResult = DoubleThroughSharedMemory(tid);
        Gpu.SyncThreads();

        output[tid] = outer[tid] + helperResult;
    }

    [GpuKernel]
    public static void CallerAndPlainCalleeSharedMemoryKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var outer = new SharedMemory<int>(256);
        outer[tid] = tid + 100;
        Gpu.SyncThreads();

        int helperResult = IncrementThroughScratch(tid);
        Gpu.SyncThreads();

        output[tid] = outer[tid] + helperResult;
    }

    [GpuKernel]
    public static void SharedMemoryPerDeclarationKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        SwapThroughDeclaration(tid + 1);
        output[tid] = SwapThroughDeclaration(0);
    }

    // --- Suspending calls under short-circuit operators ---

    // Suspends on the shuffle only, so a uniform branch around the call is legal on CUDA
    [GpuDeviceFunction]
    public static int CountedSwapWithNeighbor(int[] calls, int value)
    {
        calls[ThreadIndex.X] += 1;
        return Warp.ShuffleXorSync(FullMask, value, 1);
    }

    [GpuKernel]
    public static void ShortCircuitAndKernel(int[] input, int[] output, int[] calls, int enabled)
    {
        int tid = ThreadIndex.X;
        bool positive = enabled != 0 && CountedSwapWithNeighbor(calls, input[tid]) > 0;
        output[tid] = positive ? 1 : 0;
    }

    [GpuKernel]
    public static void ShortCircuitOrKernel(int[] input, int[] output, int[] calls, int skip)
    {
        int tid = ThreadIndex.X;
        bool positive = skip != 0 || CountedSwapWithNeighbor(calls, input[tid]) > 0;
        output[tid] = positive ? 1 : 0;
    }

    [GpuKernel]
    public static void ConditionalOperatorKernel(int[] input, int[] output, int[] calls, int useShuffle)
    {
        int tid = ThreadIndex.X;
        output[tid] = useShuffle != 0 ? CountedSwapWithNeighbor(calls, input[tid]) : input[tid] + 1000;
    }

    // --- Suspending calls in loop headers ---

    [GpuDeviceFunction]
    public static int BroadcastFromLaneZero(int value)
    {
        int broadcast = Warp.ShuffleSync(FullMask, value, 0);
        return broadcast;
    }

    // The condition is evaluated on every iteration; the bound on 'count' stops a wrongly hoisted condition from hanging
    [GpuKernel]
    public static void WhileConditionKernel(int[] output, int limit)
    {
        int tid = ThreadIndex.X;
        int count = 0;
        while (BroadcastFromLaneZero(count) < limit && count < 1000)
        {
            count++;
        }

        output[tid] = count;
    }

    [GpuKernel]
    public static void ForIncrementorKernel(int[] output, int limit)
    {
        int tid = ThreadIndex.X;
        int steps = 0;
        for (int i = 0; i < limit; i = BroadcastFromLaneZero(i + 1))
        {
            steps++;
        }

        output[tid] = steps;
    }

    [GpuKernel]
    public static void DoWhileConditionKernel(int[] output, int limit)
    {
        int tid = ThreadIndex.X;
        int count = 0;
        do
        {
            count++;
        } while (BroadcastFromLaneZero(count) < limit && count < 1000);

        output[tid] = count;
    }

    // --- Expression-bodied device functions and kernels ---

    [GpuDeviceFunction]
    public static int ShiftUpExpressionBody(int value) => Warp.ShuffleUpSync(FullMask, value, 1u);

    [GpuDeviceFunction]
    public static void StoreBroadcastExpressionBody(int[] output, int value) =>
        output[ThreadIndex.X] = Warp.ShuffleSync(FullMask, value, 0);

    [GpuKernel]
    public static void ExpressionBodyShiftUpKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = ShiftUpExpressionBody(input[tid]);
    }

    [GpuKernel]
    public static void ExpressionBodiedKernel(int[] input, int[] output) =>
        StoreBroadcastExpressionBody(output, input[ThreadIndex.X]);

    // --- By-reference parameters ---

    // Writes 'value' before the barrier and reads it after: the caller's storage must survive the resume
    [GpuDeviceFunction]
    public static void IncrementAcrossBarrier(ref int value, int[] published)
    {
        int tid = ThreadIndex.X;
        value += 1;
        SharedMemory<int> shared = new(256);
        shared[tid] = value;
        Gpu.SyncThreads();
        published[tid] = shared[tid];
        value += 10;
    }

    [GpuDeviceFunction]
    public static void IncrementTwiceAcrossBarriers(ref int value, int[] published)
    {
        IncrementAcrossBarrier(ref value, published);
        IncrementAcrossBarrier(ref value, published);
    }

    [GpuDeviceFunction]
    public static void SumWithNeighbor(int value, out int sum)
    {
        int tid = ThreadIndex.X;
        SharedMemory<int> shared = new(256);
        shared[tid] = value;
        Gpu.SyncThreads();
        sum = value + shared[tid ^ 1];
        Gpu.SyncThreads(); // Every read finishes before the next call overwrites the buffer
    }

    [GpuKernel]
    public static void RefParameterKernel(int[] input, int[] output, int[] published)
    {
        int tid = ThreadIndex.X;
        int value = input[tid];
        IncrementAcrossBarrier(ref value, published);
        output[tid] = value;
    }

    [GpuKernel]
    public static void RefParameterChainKernel(int[] input, int[] output, int[] published)
    {
        int tid = ThreadIndex.X;
        int value = input[tid];
        IncrementTwiceAcrossBarriers(ref value, published);
        output[tid] = value;
    }

    // The index has a side effect: resuming the call must not advance the cursor again
    [GpuKernel]
    public static void RefElementArgumentKernel(int[] data, int[] cursors, int[] published)
    {
        int tid = ThreadIndex.X;
        int cursor = tid * 2;
        IncrementAcrossBarrier(ref data[cursor++], published);
        cursors[tid] = cursor;
    }

    [GpuKernel]
    public static void OutParameterInLoopKernel(int[] input, int[] output, int rounds)
    {
        int tid = ThreadIndex.X;
        int value = input[tid];
        for (int round = 0; round < rounds; round++)
        {
            SumWithNeighbor(value, out int sum);
            value = sum;
        }

        output[tid] = value;
    }
}
