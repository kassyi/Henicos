// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct InternalNameCollisionPair
{
    public int A;
    public int B;
}

/// <summary>
/// Kernels whose user identifiers reuse names that the compiler has historically emitted internally.
/// </summary>
public static partial class InternalNameCollisionKernels
{
    // Locals that live across a barrier are hoisted into the per-thread context under their own names on the CPU dialect.
    [GpuKernel]
    public static void ContextFieldNamedLocalsKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int _state = data[tid];
        int _ctx = _state + 1;
        int _yieldedForGridSync = _ctx + 1;
        int _warpExchange = _yieldedForGridSync + 1;
        int _all_contexts = _warpExchange + 1;
        Gpu.SyncThreads();
        data[tid] = _state + _ctx + _yieldedForGridSync + _warpExchange + _all_contexts;
    }

    // Object initializers are emitted as an immediately invoked lambda that declares its own temporary.
    [GpuKernel]
    public static void AggregateTempNamedLocalKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int _tmp = data[tid];
        InternalNameCollisionPair pair = new InternalNameCollisionPair { A = _tmp, B = _tmp + 1 };
        data[tid] = pair.A * 100 + pair.B;
    }

    // Tuple swaps are emitted as an immediately invoked lambda that declares one temporary per element.
    [GpuKernel]
    public static void TupleTempNamedLocalsKernel(int[] first, int[] second)
    {
        int tid = ThreadIndex.X;
        int _t0 = first[tid];
        int _t1 = second[tid];
        (_t0, _t1) = (_t1, _t0);
        first[tid] = _t0;
        second[tid] = _t1;
    }

    // Loops and branches containing a barrier are lowered into goto labels on the CPU dialect.
    [GpuKernel]
    public static void LoweringLabelNamedLabelsKernel(int[] data, int rounds)
    {
        int tid = ThreadIndex.X;
        int acc = 0;
        for (int i = 0; i < rounds; i++)
        {
            acc += data[tid];
            Gpu.SyncThreads();
        }

        if (rounds > 0)
        {
            Gpu.SyncThreads();
        }

        goto _ForEnd_0;
    _ForEnd_0:
        goto _BranchEnd_0;
    _BranchEnd_0:
        data[tid] = acc;
    }

    // Warp intrinsics are hoisted into numbered temporaries before the state machine is built.
    [GpuKernel]
    public static void WarpTempNamedLocalKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int _warpTemp_0 = data[tid];
        Gpu.SyncThreads();
        int shifted = Warp.ShuffleDownSync(0xffffffffu, _warpTemp_0, 1);
        data[tid] = _warpTemp_0 * 1000 + shifted;
    }

    // Kernel parameters share a scope with the locals of the generated host launchers.
    [GpuKernel]
    public static void LauncherLocalNamedParametersKernel(int[] data, int _gsharp_contexts, int _gsharp_total_blocks,
        int _gsharp_threads_per_block, int _gsharp_total_threads, int _gsharp_launch_error, int _b_idx, int _t_idx,
        int _ctxtid, int _cudaCtxScope)
    {
        int tid = ThreadIndex.X;
        int sum = _gsharp_contexts + _gsharp_total_blocks + _gsharp_threads_per_block + _gsharp_total_threads +
                  _gsharp_launch_error + _b_idx + _t_idx + _ctxtid + _cudaCtxScope;
        Gpu.SyncThreads();
        data[tid] = sum;
    }
}
