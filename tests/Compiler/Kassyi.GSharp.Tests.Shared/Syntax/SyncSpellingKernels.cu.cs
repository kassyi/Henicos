// ReSharper disable All
using Kassyi.GSharp.Core;
using static Kassyi.GSharp.Core.FastMath;
using static Kassyi.GSharp.Core.Gpu;
using static Kassyi.GSharp.Core.ThreadIndex;
using static Kassyi.GSharp.Core.Warp;
using static Kassyi.GSharp.Tests.Shared.Syntax.SyncLookalike.UserSync;
using G = Kassyi.GSharp.Core.Gpu;
using W = Kassyi.GSharp.Core.Warp;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Barriers and warp shuffles spelled through a <c>using</c> alias or <c>using static</c>: each one splits the state
/// machine on the CPU dialects as <c>Gpu.SyncThreads()</c> / <c>Warp.ShuffleDownSync</c> do (#503), and every intrinsic
/// called through <c>using static</c> maps to its native name (#565).
/// </summary>
public static partial class SyncSpellingKernels
{
    public const int BlockSize = 4;
    public const int WarpSize = 32;

    // Each thread reads the value its right neighbour wrote before the barrier: {2, 3, 4, 1}
    [GpuKernel]
    public static void AliasBarrierRotateKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        SharedMemory<int> buffer = new(BlockSize);
        buffer[tid] = tid + 1;
        G.SyncThreads();
        output[tid] = buffer[(tid + 1) % BlockSize];
    }

    [GpuKernel]
    public static void UsingStaticBarrierRotateKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        SharedMemory<int> buffer = new(BlockSize);
        buffer[tid] = tid + 1;
        SyncThreads();
        output[tid] = buffer[(tid + 1) % BlockSize];
    }

    // Lane i reads the value of lane i + 1; the last lane keeps its own
    [GpuKernel]
    public static void AliasShuffleDownKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int value = tid + 1;
        output[tid] = W.ShuffleDownSync(0xFFFFFFFFu, value, 1u);
    }

    [GpuKernel]
    public static void UsingStaticShuffleDownKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int value = tid + 1;
        output[tid] = ShuffleDownSync(0xFFFFFFFFu, value, 1u);
    }

    [GpuKernel]
    public static void QualifiedBarrierRotateKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        SharedMemory<int> buffer = new(BlockSize);
        buffer[tid] = tid + 1;
        Kassyi.GSharp.Core.Gpu.SyncThreads();
        output[tid] = buffer[(tid + 1) % BlockSize];
    }

    // Grid syncs through an alias and using static split the state machine and mark the yield as one for a grid sync
    [GpuKernel(Cooperative = true)]
    public static void SpelledGridSyncKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = 1;
        G.SyncGrid();
        output[tid] += 1;
        SyncGrid();
        output[tid] += 1;
    }

    // Other methods named SyncThreads are ordinary calls, not barriers: output[i] = i + 10, and i + 20
    [GpuKernel]
    public static void UserSyncThreadsKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        SyncThreads(output, tid);
    }

    [GpuKernel]
    public static void LookalikeGpuSyncThreadsKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        SyncLookalike.Gpu.SyncThreads(output, tid);
    }

    // An intrinsic that does not synchronize, called through using static: every thread adds 1 to counter[0] (#565)
    [GpuKernel]
    public static void UsingStaticAtomicKernel(int[] counter)
    {
        AtomicAdd(ref counter[0], 1);
    }

    // ThreadIndex.X through using static, mapped by the pattern of its type (threadIdx.x): output[i] = 3 * i
    [GpuKernel]
    public static void UsingStaticThreadIndexKernel(int[] output)
    {
        output[X] = X * 3;
    }

    // FastMath.Exp through using static, mapped by its signature in the dialect YAML (__expf / expf)
    [GpuKernel]
    public static void UsingStaticFastExpKernel(float[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = Exp(data[tid]);
    }

    // The class's own PopCount hides Gpu.PopCount of using static, so it is not mapped to __popc: output[i] = i + 100
    [GpuDeviceFunction]
    public static int PopCount(uint value) => (int)value + 100;

    [GpuKernel]
    public static void OwnPopCountKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = PopCount((uint)tid);
    }
}
