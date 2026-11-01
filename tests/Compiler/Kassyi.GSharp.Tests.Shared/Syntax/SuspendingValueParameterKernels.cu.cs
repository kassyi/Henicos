// ReSharper disable All
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct SuspendVector2
{
    public float X;
    public float Y;
}

public struct SuspendInner
{
    public int B;
}

public struct SuspendNested
{
    public SuspendInner A;
    public int C;
}

[InlineArray(4)]
public struct SuspendBuffer4
{
    private int _element0;
}

public struct SuspendWithBuffer
{
    public SuspendBuffer4 Buffer;
    public int Tag;
}

public static class SuspendVector2Extensions
{
    [GpuDeviceFunction]
    public static void AddToX(this ref SuspendVector2 v, float d)
    {
        v.X += d;
    }
}

/// <summary>
/// Device functions that suspend after writing into a value parameter (#455). The caller passes the original arguments
/// again on every resume, so a parameter written through a field, a nested field, an <c>[InlineArray]</c> element, an
/// increment, a <c>ref</c> argument or a deconstructing assignment must live in the frame. An array parameter refers to memory that every resume
/// sees again and stays a plain argument.
/// </summary>
public static partial class SuspendingValueParameterKernels
{
    [GpuDeviceFunction]
    public static float MutateFieldThenSync(SuspendVector2 v)
    {
        v.X = v.X + 1.0f;
        Gpu.SyncThreads();
        return v.X;
    }

    // Control case: the parameter itself is assigned
    [GpuDeviceFunction]
    public static int MutateScalarThenSync(int value)
    {
        value = value + 1;
        Gpu.SyncThreads();
        return value;
    }

    [GpuDeviceFunction]
    public static int MutateNestedFieldThenSync(SuspendNested v)
    {
        v.A.B = v.A.B + 10;
        Gpu.SyncThreads();
        return v.A.B + v.C;
    }

    [GpuDeviceFunction]
    public static int MutateInlineArrayElementThenSync(SuspendWithBuffer v, int index)
    {
        v.Buffer[index] += 100;
        Gpu.SyncThreads();
        return v.Buffer[index] + v.Tag;
    }

    // The receiver of a `this ref` extension method is written (#884 review)
    [GpuDeviceFunction]
    public static float RefExtensionThenSync(SuspendVector2 v)
    {
        v.AddToX(1.0f);
        Gpu.SyncThreads();
        return v.X;
    }

    [GpuDeviceFunction]
    public static float IncrementFieldThenSync(SuspendVector2 v)
    {
        ++v.Y;
        Gpu.SyncThreads();
        return v.Y;
    }

    [GpuDeviceFunction]
    public static void AddOne(ref float target)
    {
        target = target + 1.0f;
    }

    [GpuDeviceFunction]
    public static float PassFieldByRefThenSync(SuspendVector2 v)
    {
        AddOne(ref v.X);
        Gpu.SyncThreads();
        return v.X;
    }

    [GpuDeviceFunction]
    public static int MutateArrayElementThenSync(int[] values, int index)
    {
        values[index] = values[index] + 1000;
        Gpu.SyncThreads();
        return values[index];
    }

    [GpuDeviceFunction]
    public static float TupleSwapFieldsThenSync(SuspendVector2 v)
    {
        (v.X, v.Y) = (v.Y, v.X);
        Gpu.SyncThreads();
        return v.X * 100.0f + v.Y;
    }

    [GpuDeviceFunction]
    public static int TupleSwapScalarsThenSync(int a, int b)
    {
        (a, b) = (b, a);
        Gpu.SyncThreads();
        return a * 1000 + b;
    }

    [GpuDeviceFunction]
    public static int RefTernaryParameterThenSync(int c, int paramAssign, int paramCompound, int paramInc)
    {
        int localFallback = -999;
        ((c & 1) == 0 ? ref paramAssign : ref localFallback) = 777;
        ((c & 2) == 0 ? ref paramCompound : ref localFallback) += 100;
        ++((c & 4) == 0 ? ref paramInc : ref localFallback);
        Gpu.SyncThreads();
        return paramAssign * 10000 + paramCompound * 10 + paramInc;
    }

    [GpuKernel]
    public static void StructFieldWriteKernel(float[] fieldOut, int[] scalarOut)
    {
        int tid = ThreadIndex.X;
        SuspendVector2 v = default;
        v.X = tid;
        fieldOut[tid] = MutateFieldThenSync(v);
        scalarOut[tid] = MutateScalarThenSync(tid);
    }

    [GpuKernel]
    public static void RefExtensionWriteKernel(float[] output)
    {
        int tid = ThreadIndex.X;
        SuspendVector2 v = default;
        v.X = tid;
        output[tid] = RefExtensionThenSync(v);
    }

    [GpuKernel]
    public static void NestedFieldWriteKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        SuspendNested v = default;
        v.A.B = tid;
        v.C = 1000;
        output[tid] = MutateNestedFieldThenSync(v);
    }

    [GpuKernel]
    public static void InlineArrayElementWriteKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        SuspendWithBuffer v = default;
        int index = tid & 3;
        v.Buffer[index] = tid;
        v.Tag = 10000;
        output[tid] = MutateInlineArrayElementThenSync(v, index);
    }

    [GpuKernel]
    public static void PrefixIncrementFieldKernel(float[] output)
    {
        int tid = ThreadIndex.X;
        SuspendVector2 v = default;
        v.Y = tid;
        output[tid] = IncrementFieldThenSync(v);
    }

    [GpuKernel]
    public static void RefFieldArgumentKernel(float[] output)
    {
        int tid = ThreadIndex.X;
        SuspendVector2 v = default;
        v.X = tid * 2;
        output[tid] = PassFieldByRefThenSync(v);
    }

    [GpuKernel]
    public static void ArrayElementWriteKernel(int[] values, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = MutateArrayElementThenSync(values, tid);
    }

    [GpuKernel]
    public static void TupleSwapFieldsKernel(float[] output)
    {
        int tid = ThreadIndex.X;
        SuspendVector2 v = default;
        v.X = tid;
        v.Y = 1000.0f;
        output[tid] = TupleSwapFieldsThenSync(v);
    }

    [GpuKernel]
    public static void TupleSwapScalarsKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = TupleSwapScalarsThenSync(tid, 7);
    }

    [GpuKernel]
    public static void RefTernaryParameterWriteKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = RefTernaryParameterThenSync(tid, 10, 20, 3);
    }
}
