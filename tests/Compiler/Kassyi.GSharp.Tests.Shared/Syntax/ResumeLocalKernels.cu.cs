// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Ref struct locals and stackalloc pointers that a barrier follows but that no state after the barrier uses: the CPU
/// dialects declare them again at the top of the state function instead of keeping them in the frame, which can hold
/// neither (stackalloc memory belongs to one call of the function).
/// </summary>
public static partial class ResumeLocalKernels
{
    // U2: the span is used only before the barrier: a = {1, 2}
    [GpuKernel]
    public static void SpanBeforeBarrierKernel(int[] a)
    {
        Span<int> s = stackalloc int[4];
        s[0] = 1;
        a[0] = s[0];
        Gpu.SyncThreads();
        a[1] = 2;
    }

    // U1: the span is used only on the branch without the barrier (the analyzer accepts it, C4): a = {1, 0}
    [GpuKernel]
    public static void SpanInOtherBranchKernel(int[] a, int flag)
    {
        Span<int> s = stackalloc int[4];
        if (flag > 0)
        {
            Gpu.SyncThreads();
        }
        else
        {
            s[0] = 1;
            a[0] = s[0];
        }
    }

    // The initializer reads a local assigned before the declaration: result[t] = (t odd ? second : first)[t] + 100
    [GpuKernel]
    public static void ViewAfterLocalKernel(int[] first, int[] second, int[] result)
    {
        int tid = ThreadIndex.X;
        bool useSecond = tid % 2 == 1;
        Span<int> view = useSecond ? second : first;
        result[tid] = view[tid];
        Gpu.SyncThreads();
        result[tid] += 100;
    }

    // Every execution of a stackalloc gives zeroed memory, also in the iteration that runs in the same resume as the one
    // before it: sums = {0, 0, 0}
    [GpuKernel]
    public static void StackAllocInLoopKernel(int[] sums)
    {
        for (int k = 0; k < 3; k++)
        {
            Span<int> s = stackalloc int[2];
            sums[k] = s[0] + s[1];
            s[0] = 5;
            s[1] = 7;
            if (k == 1)
            {
                Gpu.SyncThreads();
            }
        }
    }

    // T10: the size is a const local, which the frame holds: a = {1, 2}
    [GpuKernel]
    public static void ConstSizeKernel(int[] a)
    {
        const int Size = 4;
        Span<int> s = stackalloc int[Size];
        s[Size - 1] = 1;
        a[0] = s[Size - 1];
        Gpu.SyncThreads();
        a[1] = 2;
    }

    // T1: a stackalloc pointer is incremented and assigned like the C# pointer: a = {1, 2}
    [GpuKernel]
    public static unsafe void MovingPointerKernel(int[] a)
    {
        int* p = stackalloc int[4];
        *p++ = 1;
        *p = 2;
        a[0] = p[-1];
        a[1] = p[0];
    }

    // T2: the same before a barrier: a = {1, 2, 3}
    [GpuKernel]
    public static unsafe void MovingPointerBeforeBarrierKernel(int[] a)
    {
        int* p = stackalloc int[4];
        *p++ = 1;
        *p = 2;
        a[0] = p[-1];
        a[1] = p[0];
        Gpu.SyncThreads();
        a[2] = 3;
    }

    // A read-only stackalloc span with elements before a barrier: a = {30, 1}
    [GpuKernel]
    public static void ReadOnlyElementsKernel(int[] a)
    {
        ReadOnlySpan<int> r = stackalloc int[] { 10, 20, 30 };
        a[0] = r[2];
        Gpu.SyncThreads();
        a[1] = 1;
    }

    // The lowering flattens sibling blocks into one, so the barrier of the second block follows the span of the first:
    // a[t] = 3
    [GpuKernel]
    public static void SiblingScopeSpanKernel(int[] a)
    {
        int acc = 1;
        {
            Gpu.SyncThreads();
            Span<int> s = stackalloc int[2];
            s[1] = acc;
            acc += s[1];
        }

        {
            Gpu.SyncThreads();
            acc += 1;
        }

        a[ThreadIndex.X] = acc;
    }

    // Two spans of the same name in sibling blocks: a[t] = 4
    [GpuKernel]
    public static void SameNameSpansKernel(int[] a)
    {
        int acc = 1;
        {
            Span<int> s = stackalloc int[2];
            s[0] = acc;
            acc += s[0];
            Gpu.SyncThreads();
        }

        {
            Span<int> s = stackalloc int[3];
            s[2] = acc;
            acc += s[2];
            Gpu.SyncThreads();
        }

        a[ThreadIndex.X] = acc;
    }

    // U3: the same with a stackalloc pointer: a = {3, 4}
    [GpuKernel]
    public static unsafe void PointerBeforeBarrierKernel(int[] a)
    {
        int* p = stackalloc int[4];
        p[0] = 3;
        a[0] = p[0];
        Gpu.SyncThreads();
        a[1] = 4;
    }

    // The length of a stackalloc whose size is a constant expression of a const local, read before a barrier: a = {4, 2}
    [GpuKernel]
    public static void ConstSizeLengthKernel(int[] a)
    {
        const int Half = 2;
        Span<int> s = stackalloc int[Half * 2];
        a[0] = s.Length;
        Gpu.SyncThreads();
        a[1] = 2;
    }

    // A stackalloc pointer of length 0, which has no C++ array: a = {5}
    [GpuKernel]
    public static unsafe void ZeroLengthPointerKernel(int[] a)
    {
        int* p = stackalloc int[0];
        a[0] = (int)(p + 0 - p) + 5;
    }

    // The same before a barrier: a = {5, 6}
    [GpuKernel]
    public static unsafe void ZeroLengthPointerBeforeBarrierKernel(int[] a)
    {
        int* p = stackalloc int[0];
        a[0] = (int)(p + 0 - p) + 5;
        Gpu.SyncThreads();
        a[1] = 6;
    }

    // A stackalloc pointer without elements is zeroed like any other stack array (#748): a = {0}
    [GpuKernel]
    public static unsafe void PointerZeroedKernel(int[] a)
    {
        int* p = stackalloc int[4];
        a[0] = p[0] + p[3];
    }

    // A stackalloc span of length 0, which has no C++ array: a = {5}
    [GpuKernel]
    public static void ZeroLengthSpanKernel(int[] a)
    {
        Span<int> s = stackalloc int[0];
        a[0] = s.Length + 5;
    }

    // The same before a barrier: a = {5, 6}
    [GpuKernel]
    public static void ZeroLengthSpanBeforeBarrierKernel(int[] a)
    {
        Span<int> s = stackalloc int[0];
        a[0] = s.Length + 5;
        Gpu.SyncThreads();
        a[1] = 6;
    }

    // A stackalloc pointer with elements and no barrier: a = {15}
    [GpuKernel]
    public static unsafe void PointerElementsKernel(int[] a)
    {
        int* p = stackalloc int[] { 7, 8 };
        a[0] = p[0] + p[1];
    }
}
