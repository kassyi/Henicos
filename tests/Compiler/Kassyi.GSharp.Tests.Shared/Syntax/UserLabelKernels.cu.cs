// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels with labels of the user and <c>goto</c>. The CPU dialects lower a kernel into a state machine and must keep
/// the labels, the locals declared before them and the split points under them as CUDA runs them.
/// </summary>
public static partial class UserLabelKernels
{
    private const uint FullMask = 0xFFFFFFFFu;

    // The shuffle under the label runs again on every goto
    [GpuKernel]
    public static void LabeledShuffleLoopKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int v = data[tid];
        int acc = 0;
        int k = 0;
    again:
        acc += Warp.ShuffleDownSync(FullMask, v, 1);
        v += 1;
        if (++k < 3)
        {
            goto again;
        }

        data[tid] = acc;
    }

    // A label without a barrier: the locals declared before it stay in scope after it
    [GpuKernel]
    public static void LabelLoopKernel(int[] data, int rounds)
    {
        int tid = ThreadIndex.X;
        int acc = 0;
        int i = 0;
    loop:
        if (i < rounds)
        {
            acc += data[tid];
            i++;
            goto loop;
        }

        data[tid] = acc;
    }

    // The goto is in a later state than its label (after the barrier), so the label must start a state
    [GpuKernel]
    public static void BranchExitLabelKernel(int[] data, int flag)
    {
        int tid = ThreadIndex.X;
        int value = data[tid];
        if (flag != 0)
        {
            Gpu.SyncThreads();
            value += 1;
            goto done;
        }

        value += 10;
    done:
        data[tid] = value * 2;
    }

    // On the first look b and its goto are in one state; once a starts a state, the goto b is in a later state than b
    [GpuKernel]
    public static void ChainedLabelKernel(int[] data, int flag)
    {
        int tid = ThreadIndex.X;
        int x = data[tid];
        if (flag != 0)
        {
            Gpu.SyncThreads();
            x += 100;
            goto a;
        }

        x += 1;
    b:
        x += 2;
    a:
        x += 3;
        if (x < 20)
        {
            goto b;
        }

        data[tid] = x;
    }

#pragma warning disable CS0164 // The label under test has no goto
    // The barrier under the label still separates the write of every thread from the reads of its neighbour
    [GpuKernel]
    public static void LabeledBarrierKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var buffer = new SharedMemory<int>(4);
        buffer[tid] = tid + 1;
    sync:
        Gpu.SyncThreads();
        output[tid] = buffer[(tid + 1) % 4];
    }
#pragma warning restore CS0164
}
