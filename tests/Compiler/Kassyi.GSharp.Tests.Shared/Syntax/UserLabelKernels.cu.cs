// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels with labels of the user and <c>goto</c>. The CPU dialects lower a kernel into a state machine and must keep
/// the labels, the locals declared before them and the split points under them as CUDA runs them.
/// </summary>
/// <summary>A struct with a constructor, declared past a goto in <see cref="UserLabelKernels.GotoPastInitializationKernel"/>.</summary>
public struct LabelPair
{
    public int A;
    public int B;

    public LabelPair(int a, int b)
    {
        A = a;
        B = b;
    }
}

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

    // extra is declared before a label that starts a state and is used after it, with no barrier in between.
    // [WHY] The locals after the goto are declared without an initializer: a goto past an initialized declaration is
    // ill-formed C++ ([stmt.dcl]); nvcc warns (#546-D) and g++ rejects it.
    [GpuKernel]
    public static void CrossLabelWriteKernel(int[] data, int flag)
    {
        int tid = ThreadIndex.X;
        int value = data[tid];
        if (flag != 0)
        {
            Gpu.SyncThreads();
            value += 1;
            goto done;
        }

        int before;
        before = 3;
        value += before;
        int extra;
        extra = 7;
        value += extra;
    done:
        extra = value * 2;
        data[tid] = extra;
    }

    // The goto jumps past initialized declarations (an int through var, and a struct). C# allows it because nothing reads
    // them after the label; C++ does not ([stmt.dcl]), so the declarations are emitted as a declaration and an assignment
    [GpuKernel]
    public static void GotoPastInitializationKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        if (data[tid] % 3 == 0)
        {
            goto skip;
        }

        var doubled = data[tid] * 2;
        LabelPair pair = new LabelPair(doubled, 1);
        data[tid] = pair.A + pair.B;
    skip:
        data[tid] += 1;
    }

    // The goto leaves a nested block and jumps past a const local, an array and a stackalloc span: C# zeroes both
    // arrays, so their zero fill stays at the declaration
    [GpuKernel]
    public static void GotoPastConstAndArraysKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int total = 0;
        if (data[tid] % 4 == 0)
        {
            {
                total += 100;
                goto skip;
            }
        }

        const int Scale = 3;
        int[] counts = new int[4];
        Span<int> scratch = stackalloc int[4];
        counts[1] += data[tid] * Scale;
        scratch[2] += counts[1] + 1;
        total += counts[0] + counts[1] + scratch[1] + scratch[2];
    skip:
        total += 1;
        data[tid] = total;
    }

    // Two sibling scopes with a barrier and a label of the same name each, and a stackalloc span used after the label.
    // The labels start no state, so the span stays a local of its state. [WHY] Only the last scope has a span: a
    // stackalloc span in a scope that lowering flattens in front of a later barrier is hoisted by the barrier rule, which
    // the CPU paths cannot do (a separate problem, with or without labels)
    [GpuKernel]
    public static void SiblingSameLabelKernel(int[] data, int flag)
    {
        int tid = ThreadIndex.X;
        int acc = data[tid];
        {
            Gpu.SyncThreads();
            int first0 = acc;
            int first1 = 0;
            if ((tid & 1) == flag)
            {
                acc += first0 + 10;
                goto done;
            }

            acc += first1 + 100;
        done:
            acc += first0 + 1;
        }

        {
            Gpu.SyncThreads();
            Span<int> second = stackalloc int[2];
            second[1] = acc;
            if ((tid & 1) != flag)
            {
                acc += second[1] * 2;
                goto done;
            }

            acc += 1000;
        done:
            acc += second[1] + 2;
        }

        data[tid] = acc;
    }

    // Two labels of one name end up in one state: the first scope starts with a barrier, the second ends with one. The
    // managed output declares both in one block, so it must rename them (CS0140 otherwise)
    [GpuKernel]
    public static void SameStateSameLabelKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int acc = data[tid];
        {
            int step = 10;
            Gpu.SyncThreads();
            if ((tid & 1) == 0)
            {
                acc += step;
                goto done;
            }

            acc += 100;
        done:;
        }

        {
            int step = 1000;
            if ((tid & 1) != 0)
            {
                acc += step;
                goto done;
            }

            acc += 2000;
        done:;
            Gpu.SyncThreads();
        }

        data[tid] = acc;
    }

    // The shape of CrossLabelWriteKernel with an initialized declaration: the CPU paths hoist extra into the context,
    // CUDA splits the declaration
    [GpuKernel]
    public static void CrossLabelInitializedKernel(int[] data, int flag)
    {
        int tid = ThreadIndex.X;
        int value = data[tid];
        if (flag != 0)
        {
            Gpu.SyncThreads();
            value += 1;
            goto done;
        }

        int extra = 10;
        value += extra;
    done:
        extra = value * 2;
        data[tid] = extra;
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
