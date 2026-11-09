using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct FieldPair
{
    public int X;
    public int Y;
}

public struct FieldOuter
{
    public FieldPair Inner;
}

public static partial class ElementFieldWriteKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ArrayFieldKernel(FieldPair[] a)
    {
        a[ThreadIndex.X].X = 1;
        if (a[0].X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CompoundFieldKernel(FieldPair[] a)
    {
        a[ThreadIndex.X].X += 2;
        if (a[0].X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void IncrementFieldKernel(FieldPair[] a)
    {
        a[ThreadIndex.X].X++;
        if (a[0].X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NestedFieldKernel(FieldOuter[] a)
    {
        a[ThreadIndex.X].Inner.X = 1;
        if (a[0].Inner.X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void SpanFieldKernel(FieldPair[] a)
    {
        Span<FieldPair> s = a;
        s[ThreadIndex.X].X = 1;
        if (s[0].X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void SharedFieldKernel()
    {
        var shared = new SharedMemory<FieldPair>(32);
        shared[ThreadIndex.X].X = 1;
        if (shared[0].X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // Written by every thread, then a barrier: the read is the same for every thread of the block, no diagnostic
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void FieldWriteThenBarrierKernel(FieldPair[] a)
    {
        a[ThreadIndex.X].X = 1;
        Gpu.SyncThreads();
        if (a[0].X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // A field of a local copy of an element is the thread's own, not the array: no diagnostic
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void LocalFieldKernel(FieldPair[] a)
    {
        FieldPair p = a[0];
        p.X = ThreadIndex.X;
        if (a[0].X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // The element written as a whole: the form that was already reported
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ElementKernel(int[] a)
    {
        a[ThreadIndex.X] = 1;
        if (a[0] == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }
}

public struct MutablePair
{
    public int X;

    public int P { get; set; }

    public void Bump()
    {
        X++;
    }

    public readonly int Peek()
    {
        return X;
    }
}

public static partial class ElementFieldWriteKernels
{
    // An auto-property of an element written: its backing field is part of the element
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void AutoPropertyKernel(MutablePair[] a)
    {
        a[ThreadIndex.X].P = 1;
        if (a[0].P == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // A struct member that writes this, called on an element
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void MutatingMethodKernel(MutablePair[] a)
    {
        a[ThreadIndex.X].Bump();
        if (a[0].X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // A readonly member called on an element writes nothing: no diagnostic
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ReadonlyMethodKernel(MutablePair[] a, int[] result)
    {
        result[ThreadIndex.X] = a[ThreadIndex.X].Peek();
        if (a[0].X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }
}
