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

public struct WriteMacroContext
{
    public int Calls;
}

public static class WriteMacroOps
{
    [GpuDeviceFunction]
    public static void AddWithRefContext(ref WriteMacroContext ctx, out WriteMacroValue result, WriteMacroValue a,
        WriteMacroValue b)
    {
        ctx.Calls += 1;
        result = new WriteMacroValue { Value = a.Value + b.Value + ctx.Calls };
    }

    [GpuDeviceFunction]
    public static void ShiftWithValueContext(WriteMacroContext ctx, out WriteMacroValue result, WriteMacroValue a,
        int shift)
    {
        result = new WriteMacroValue { Value = (a.Value << shift) + ctx.Calls };
    }

    [GpuDeviceFunction]
    public static void NegateWithInContext(in WriteMacroContext ctx, out WriteMacroValue result, WriteMacroValue a)
    {
        result = new WriteMacroValue { Value = -a.Value + ctx.Calls };
    }
}

public struct WriteMacroValue
{
    public int Value;

    [GpuOperatorMacro(typeof(WriteMacroOps), nameof(WriteMacroOps.AddWithRefContext))]
    public static WriteMacroValue operator +(WriteMacroValue a, WriteMacroValue b) =>
        throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(WriteMacroOps), nameof(WriteMacroOps.ShiftWithValueContext))]
    public static WriteMacroValue operator <<(WriteMacroValue a, int shift) =>
        throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(WriteMacroOps), nameof(WriteMacroOps.NegateWithInContext))]
    public static WriteMacroValue operator -(WriteMacroValue a) =>
        throw new GpuOnlyException();
}

// The receiver of an extension method is written only through a `this ref` parameter
public static class WriteVectorExtensions
{
    [GpuDeviceFunction]
    public static void AddX(this ref WriteVector v, float d)
    {
        v.X += d;
    }

    [GpuDeviceFunction]
    public static float SumIn(this in WriteVector v) => v.X + v.Y;

    [GpuDeviceFunction]
    public static float SumRefReadOnly(this ref readonly WriteVector v) => v.X + v.Y;

    [GpuDeviceFunction]
    public static float SumByValue(this WriteVector v) => v.X + v.Y;
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
    public static float RefExtensionCall(WriteVector v)
    {
        v.AddX(1.0f);
        Gpu.SyncThreads();
        return v.X;
    }

    [GpuDeviceFunction]
    public static float NonRefExtensionCalls(WriteVector v)
    {
        float sum = v.SumIn() + v.SumRefReadOnly() + v.SumByValue();
        Gpu.SyncThreads();
        return sum + v.X;
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

    [GpuDeviceFunction]
    public static int RefTernaryScalarAssignment(bool pickFirst, int a, int b)
    {
        int local = 0;
        (pickFirst ? ref a : ref local) = 1;
        Gpu.SyncThreads();
        return a + b + local;
    }

    [GpuDeviceFunction]
    public static int RefTernaryBothBranchesParameters(bool pickFirst, int a, int b)
    {
        (pickFirst ? ref a : ref b) += 1;
        Gpu.SyncThreads();
        return a + b;
    }

    [GpuDeviceFunction]
    public static int RefTernaryIncrement(bool pickFirst, int a)
    {
        int local = 0;
        ++(pickFirst ? ref a : ref local);
        Gpu.SyncThreads();
        return a + local;
    }

    [GpuDeviceFunction]
    public static float RefTernaryFieldAssignment(bool pickFirst, WriteVector v)
    {
        WriteVector local = default;
        (pickFirst ? ref v : ref local).X = 1.0f;
        Gpu.SyncThreads();
        return v.X + local.X;
    }

    [GpuDeviceFunction]
    public static int RefTernaryMutatingMethodCall(bool pickFirst, WriteCounter v)
    {
        WriteCounter local = default;
        (pickFirst ? ref v : ref local).Bump();
        Gpu.SyncThreads();
        return v.Count + local.Count;
    }

    [GpuDeviceFunction]
    public static float RefTernaryRefArgument(bool pickFirst, WriteVector v)
    {
        float local = 0.0f;
        AddOne(ref (pickFirst ? ref v.X : ref local));
        Gpu.SyncThreads();
        return v.X + local;
    }

    [GpuDeviceFunction]
    public static int OperatorMacroRefContextParameter(WriteMacroContext ctx, WriteMacroValue a, WriteMacroValue b)
    {
        WriteMacroValue c;
        c = a + b;
        Gpu.SyncThreads();
        return ctx.Calls + c.Value;
    }

    [GpuDeviceFunction]
    public static int OperatorMacroNestedRefContextParameter(WriteMacroContext ctx, WriteMacroValue a, int shift)
    {
        WriteMacroValue c;
        c = (a + a) << shift;
        Gpu.SyncThreads();
        return ctx.Calls + c.Value;
    }

    [GpuDeviceFunction]
    public static int OperatorMacroValueContextParameter(WriteMacroContext ctx, WriteMacroValue a, int shift)
    {
        WriteMacroValue c;
        c = a << shift;
        Gpu.SyncThreads();
        return ctx.Calls + c.Value;
    }

    [GpuDeviceFunction]
    public static int OperatorMacroInContextParameter(WriteMacroContext ctx, WriteMacroValue a)
    {
        WriteMacroValue c;
        c = -a;
        Gpu.SyncThreads();
        return ctx.Calls + c.Value;
    }
}
