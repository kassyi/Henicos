// ReSharper disable All
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct WithPlainStruct
{
    public int X;
    public int Y;
    public int Z;
}

public readonly struct WithReadonlyStruct
{
    public int X { get; init; }
    public int Y { get; init; }
}

public record struct WithRecordStruct(int X, int Y);

public readonly record struct WithReadonlyRecordStruct(int X, int Y);

public struct WithOuterStruct
{
    public WithPlainStruct Inner;
    public int Tag;
}

public struct WithArrayStruct
{
    public int[] Data;
    public int Count;
}

public struct WithPairStruct<T> where T : unmanaged
{
    public T First;
    public T Second;
}

public ref struct WithRefStruct
{
    public int X;
    public int Y;
}

[InlineArray(4)]
public struct WithFourInts
{
    private int _element0;
}

public unsafe struct WithFixedBufferStruct
{
    public fixed int Buffer[4];
    public int Tag;
}

public static partial class StructWithExpressionKernels
{
    [GpuDeviceFunction]
    public static int Bump(int[] counter)
    {
        counter[0] += 1;
        return counter[0];
    }

    [GpuDeviceFunction]
    public static WithPlainStruct MakePlain(WithPlainStruct seed, int[] counter)
    {
        counter[0] += 1;
        return seed;
    }

    [GpuDeviceFunction]
    public static WithPlainStruct ShiftX(WithPlainStruct p, int delta) => p with { X = p.X + delta };

    [GpuKernel]
    public static void PlainStructKernel(int[] r)
    {
        var p = new WithPlainStruct { X = 1, Y = 2, Z = 3 };
        var q = p with { X = 10, Z = 30 };
        r[0] = q.X;
        r[1] = q.Y;
        r[2] = q.Z;
        // the source is unchanged
        r[3] = p.X;
        r[4] = p.Y;
        r[5] = p.Z;
    }

    [GpuKernel]
    public static void EmptyInitializerKernel(int[] r)
    {
        var p = new WithPlainStruct { X = 4, Y = 5, Z = 6 };
        var q = p with { };
        q.X = 99;
        r[0] = p.X;
        r[1] = q.X;
        r[2] = q.Y;
    }

    [GpuKernel]
    public static void ReadonlyStructKernel(int[] r)
    {
        var p = new WithReadonlyStruct { X = 1, Y = 2 };
        var q = p with { Y = 20 };
        r[0] = q.X;
        r[1] = q.Y;
        r[2] = p.Y;
    }

    [GpuKernel]
    public static void RecordStructKernel(int[] r)
    {
        var p = new WithRecordStruct(1, 2);
        var q = p with { X = 10 };
        r[0] = q.X;
        r[1] = q.Y;
        r[2] = p.X;
    }

    [GpuKernel]
    public static void ReadonlyRecordStructKernel(int[] r)
    {
        var p = new WithReadonlyRecordStruct(1, 2);
        var q = p with { Y = 20 };
        r[0] = q.X;
        r[1] = q.Y;
        r[2] = p.Y;
    }

    [GpuKernel]
    public static void NestedStructKernel(int[] r)
    {
        var p = new WithOuterStruct { Inner = new WithPlainStruct { X = 1, Y = 2, Z = 3 }, Tag = 7 };
        var q = p with { Inner = p.Inner with { X = 100 }, Tag = 8 };
        r[0] = q.Inner.X;
        r[1] = q.Inner.Y;
        r[2] = q.Tag;
        r[3] = p.Inner.X;
        r[4] = p.Tag;
    }

    [GpuKernel]
    public static void ChainedWithKernel(int[] r)
    {
        var p = new WithPlainStruct { X = 1, Y = 2, Z = 3 };
        var q = (p with { X = 10 }) with { Y = 20 };
        r[0] = q.X;
        r[1] = q.Y;
        r[2] = q.Z;
    }

    [GpuKernel]
    public static void ArrayFieldKernel(int[] r)
    {
        var a = new WithArrayStruct { Data = r, Count = 1 };
        var b = a with { Count = 5 };
        // a shallow copy: both structs share the array
        b.Data[0] = 7;
        r[1] = a.Data[0];
        r[2] = a.Count;
        r[3] = b.Count;
    }

    [GpuKernel]
    public static void GenericStructKernel(int[] r)
    {
        // One instantiation only: the device definition of a generic struct is emitted per instantiation under one guard,
        // so a second instantiation (Pair<float>) would get the layout of the first, with or without 'with'
        var p = new WithPairStruct<int> { First = 1, Second = 2 };
        var q = p with { Second = 20 };
        r[0] = q.First;
        r[1] = q.Second;
        r[2] = p.Second;
    }

    [GpuKernel]
    public static void RefStructKernel(int[] r)
    {
        var p = new WithRefStruct { X = 1, Y = 2 };
        var q = p with { X = 10 };
        r[0] = q.X;
        r[1] = q.Y;
        r[2] = p.X;
    }

    [GpuKernel]
    public static void InlineArrayStructKernel(int[] r)
    {
        var a = new WithFourInts();
        a[0] = 5;
        a[3] = 8;
        var b = a with { };
        b[0] = 50;
        r[0] = a[0];
        r[1] = a[3];
        r[2] = b[0];
        r[3] = b[3];
    }

    [GpuKernel]
    public static unsafe void FixedBufferStructKernel(int[] r)
    {
        var a = new WithFixedBufferStruct();
        a.Buffer[0] = 5;
        a.Buffer[2] = 6;
        a.Tag = 1;
        var b = a with { Tag = 2 };
        b.Buffer[0] = 50;
        r[0] = a.Buffer[0];
        r[1] = b.Buffer[0];
        r[2] = b.Buffer[2];
        r[3] = a.Tag;
        r[4] = b.Tag;
    }

    [GpuKernel]
    public static void OperandEvaluatedOnceKernel(int[] r, int[] counter)
    {
        var q = MakePlain(new WithPlainStruct { X = 10, Y = 20, Z = 30 }, counter) with { X = 1, Y = 2 };
        r[0] = counter[0];
        r[1] = q.X;
        r[2] = q.Y;
        r[3] = q.Z;
    }

    [GpuKernel]
    public static void AssignmentOrderKernel(int[] r, int[] counter)
    {
        var p = new WithPlainStruct { X = 100, Y = 200, Z = 300 };
        // the initializers run in the written order and read the original value
        var q = p with { X = Bump(counter), Y = Bump(counter), Z = p.X + p.Y };
        r[0] = q.X;
        r[1] = q.Y;
        r[2] = q.Z;
    }

    [GpuKernel]
    public static void ExpressionPositionsKernel(int[] r, int flag)
    {
        var p = new WithPlainStruct { X = 1, Y = 2, Z = 3 };
        var viaCall = ShiftX(p, 5);
        var viaTernary = flag != 0 ? p with { X = 100 } : p with { X = 200 };
        r[0] = viaCall.X;
        r[1] = viaTernary.X;
        r[2] = ShiftX(p with { Y = 9 }, 1).Y;
        r[3] = (p with { Z = 33 }).Z;
    }

    [GpuKernel]
    public static void AcrossBarrierKernel(int[] r)
    {
        int tid = ThreadIndex.X;
        var p = new WithPlainStruct { X = tid, Y = 2, Z = 3 };
        Gpu.SyncThreads();
        var q = p with { Y = tid + 100 };
        Gpu.SyncThreads();
        var s = q with { Z = q.X + q.Y };
        r[tid] = s.Z;
    }

    // A device function that suspends at a barrier keeps the copy in its frame
    [GpuDeviceFunction]
    public static int SuspendingWith(WithPlainStruct p, int tid)
    {
        var q = p with { Y = tid + 1 };
        Gpu.SyncThreads();
        return (q with { Z = 5 }).Y + q.X + q.Z;
    }

    [GpuKernel]
    public static void SuspendingDeviceFunctionKernel(int[] r)
    {
        int tid = ThreadIndex.X;
        var p = new WithPlainStruct { X = 10, Y = 0, Z = 3 };
        r[tid] = SuspendingWith(p, tid);
    }
}
