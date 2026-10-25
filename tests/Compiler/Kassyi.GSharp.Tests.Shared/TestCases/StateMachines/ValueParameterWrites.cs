using System;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public struct WriteVector
{
    public float X;
    public float Y;
}

public struct WriteInner
{
    public int B;
}

public struct WriteNested
{
    public WriteInner A;
}

[InlineArray(4)]
public struct WriteBuffer4
{
    private int _element0;
}

public struct WriteWithBuffer
{
    public WriteBuffer4 Buffer;
}

public unsafe struct WriteWithFixedBuffer
{
    public fixed int Values[4];
}

public struct WriteWithArray
{
    public int[] Values;
}

public unsafe struct WriteWithPointer
{
    public int* Values;
}

public struct WriteCounter
{
    public int Count;

    public void Bump()
    {
        Count = Count + 1;
    }

    public readonly int Peek() => Count;
}

public readonly struct WriteReadOnlyCounter
{
    public readonly int Count;

    public int Peek() => Count;
}

public struct WriteNestedCounter
{
    public WriteCounter Inner;
}

public static class ValueParameterWrites
{
    [GpuDeviceFunction]
    public static void AddOne(ref float target)
    {
        target = target + 1.0f;
    }

    [GpuDeviceFunction]
    public static void SetOne(out float target)
    {
        target = 1.0f;
    }

    [GpuDeviceFunction]
    public static float FieldAssignment(WriteVector v)
    {
        v.X = 1.0f;
        Gpu.SyncThreads();
        return v.X;
    }

    [GpuDeviceFunction]
    public static float FieldCompoundAssignment(WriteVector v)
    {
        v.X += 1.0f;
        Gpu.SyncThreads();
        return v.X;
    }

    [GpuDeviceFunction]
    public static int NestedFieldAssignment(WriteNested v)
    {
        v.A.B = 1;
        Gpu.SyncThreads();
        return v.A.B;
    }

    [GpuDeviceFunction]
    public static int InlineArrayElementAssignment(WriteWithBuffer v, int index)
    {
        v.Buffer[index] = 1;
        Gpu.SyncThreads();
        return v.Buffer[index];
    }

    [GpuDeviceFunction]
    public static unsafe int FixedBufferElementAssignment(WriteWithFixedBuffer v, int index)
    {
        v.Values[index] = 1;
        Gpu.SyncThreads();
        return v.Values[index];
    }

    [GpuDeviceFunction]
    public static float FieldPrefixIncrement(WriteVector v)
    {
        ++v.X;
        Gpu.SyncThreads();
        return v.X;
    }

    [GpuDeviceFunction]
    public static float FieldPostfixDecrement(WriteVector v)
    {
        v.Y--;
        Gpu.SyncThreads();
        return v.Y;
    }

    [GpuDeviceFunction]
    public static float FieldRefArgument(WriteVector v)
    {
        AddOne(ref v.X);
        Gpu.SyncThreads();
        return v.X;
    }

    [GpuDeviceFunction]
    public static float FieldOutArgument(WriteVector v)
    {
        SetOne(out v.Y);
        Gpu.SyncThreads();
        return v.Y;
    }

    [GpuDeviceFunction]
    public static int ArrayElementAssignment(int[] values, int index)
    {
        values[index] = 1;
        Gpu.SyncThreads();
        return values[index];
    }

    [GpuDeviceFunction]
    public static int ArrayFieldElementAssignment(WriteWithArray v, int index)
    {
        v.Values[index] = 1;
        Gpu.SyncThreads();
        return v.Values[index];
    }

    [GpuDeviceFunction]
    public static int SpanElementAssignment(Span<int> values, int index)
    {
        values[index] = 1;
        Gpu.SyncThreads();
        return values[index];
    }

    [GpuDeviceFunction]
    public static float TupleSwapFields(WriteVector v)
    {
        (v.X, v.Y) = (v.Y, v.X);
        Gpu.SyncThreads();
        return v.X;
    }

    [GpuDeviceFunction]
    public static int TupleSwapScalars(int a, int b)
    {
        (a, b) = (b, a);
        Gpu.SyncThreads();
        return a - b;
    }

    [GpuDeviceFunction]
    public static float NestedTupleAssignment(WriteVector v, int a, int unused)
    {
        ((v.X, a), v.Y) = ((1.0f, 2), 3.0f);
        Gpu.SyncThreads();
        return v.X + a + v.Y + unused;
    }

    [GpuDeviceFunction]
    public static int MutatingMethodCall(WriteCounter v)
    {
        v.Bump();
        Gpu.SyncThreads();
        return v.Count;
    }

    [GpuDeviceFunction]
    public static int NestedFieldMutatingMethodCall(WriteNestedCounter v)
    {
        v.Inner.Bump();
        Gpu.SyncThreads();
        return v.Inner.Count;
    }

    [GpuDeviceFunction]
    public static int ReadOnlyMethodCall(WriteCounter v)
    {
        int count = v.Peek();
        Gpu.SyncThreads();
        return count + v.Count;
    }

    [GpuDeviceFunction]
    public static int ReadOnlyStructMethodCall(WriteReadOnlyCounter v)
    {
        int count = v.Peek();
        Gpu.SyncThreads();
        return count + v.Count;
    }

    [GpuDeviceFunction]
    public static unsafe int PointerElementAssignment(int* values, int index)
    {
        values[index] = 1;
        Gpu.SyncThreads();
        return values[index];
    }

    [GpuDeviceFunction]
    public static unsafe int PointerFieldElementAssignment(WriteWithPointer v, int index)
    {
        v.Values[index] = 1;
        Gpu.SyncThreads();
        return v.Values[index];
    }

    [GpuDeviceFunction]
    public static float FieldReadOnly(WriteVector v)
    {
        float x = v.X;
        Gpu.SyncThreads();
        return x + v.Y;
    }
}
