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
