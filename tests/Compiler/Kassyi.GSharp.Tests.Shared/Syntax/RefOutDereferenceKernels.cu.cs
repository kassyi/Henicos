// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct RefTestStruct
{
    public int Field;
    public int Value;

    public RefTestStruct(int field, int value)
    {
        Field = field;
        Value = value;
        RefOutDereferenceKernels.MemberAccessRef(ref this);
    }

    public int ComputedSum => Field + Value;

    public int PassComputedToIn => RefOutDereferenceKernels.ReadInInt(ComputedSum);
}

public struct AutoPropHolder
{
    public int AutoProp { get; set; }
    public RefTestStruct NestedProp { get; set; }
}

public static partial class RefOutDereferenceKernels
{
    private const int ConstVal = 15;

    [GpuDeviceFunction]
    public static void ModifyRef(ref bool successFlag)
    {
        successFlag = false;
    }

    [GpuDeviceFunction]
    public static void MemberAccessRef(ref RefTestStruct refArg)
    {
        refArg.Field = 5;
    }

    [GpuDeviceFunction]
    public static void ChainedAssignmentRef(ref int refArg, out int a, out int b)
    {
        a = b = refArg = 10;
    }

    [GpuDeviceFunction]
    public static int ReadRefReadOnlyInt(ref readonly int value)
    {
        return value + 10;
    }

    [GpuDeviceFunction]
    public static int ReadInInt(in int value)
    {
        return value + 20;
    }

    [GpuDeviceFunction]
    public static int ReadInLong(in long value)
    {
        return (int)(value + 30L);
    }

    [GpuDeviceFunction]
    public static int ReadInDouble(in double value)
    {
        return (int)(value * 2.0);
    }

    [GpuDeviceFunction]
    public static int ForwardRefReadOnlyInt(ref readonly int value)
    {
        return ReadRefReadOnlyInt(in value) + ReadInInt(in value) + ReadInInt(value);
    }

    [GpuDeviceFunction]
    public static int ForwardInInt(in int value)
    {
        return ReadInInt(in value) + ReadRefReadOnlyInt(in value) + ReadInInt(value);
    }

    [GpuDeviceFunction]
    public static int ReadRefReadOnlyStruct(ref readonly RefTestStruct s)
    {
        return s.Field * 100 + s.Value;
    }

    [GpuDeviceFunction]
    public static int ReadInStruct(in RefTestStruct s)
    {
        return s.Field * 10 + s.Value;
    }

    [GpuDeviceFunction]
    public static int ForwardRefReadOnlyAndInStruct(ref readonly RefTestStruct a, in RefTestStruct b)
    {
        return ReadRefReadOnlyStruct(in a) + ReadInStruct(in b) + ReadInStruct(a) + ReadRefReadOnlyStruct(in b);
    }

    [GpuDeviceFunction]
    public static int MutateHolderAndReadInSnapshot(ref AutoPropHolder h, in int snap)
    {
        h.AutoProp = 99;
        return snap;
    }

    [GpuDeviceFunction]
    public static int MutateHolderAndReadInStructSnapshot(ref AutoPropHolder h, in RefTestStruct snap)
    {
        h.NestedProp = new RefTestStruct { Field = 8, Value = 8 };
        return snap.Field * 10 + snap.Value;
    }

    [GpuKernel]
    public static void RefOutTestKernel(int[] data)
    {
        bool flag = true;
        ModifyRef(ref flag);

        RefTestStruct s = new RefTestStruct { Field = 0, Value = 10 };
        MemberAccessRef(ref s);

        int a, b;
        int val = 1;
        ChainedAssignmentRef(ref val, out a, out b);

        data[0] = (flag ? 100 : 0) + s.Field + a + b + val;
    }

    [GpuKernel]
    public static void RefReadOnlyAndInParameterKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        int x = input[tid];
        int y = x + 3;

        RefTestStruct s1 = new RefTestStruct { Field = x, Value = 7 };
        RefTestStruct s2 = new RefTestStruct { Field = y, Value = 9 };

        output[tid * 4] = ReadRefReadOnlyInt(in x) * 1000 + ReadInInt(in y);
        output[tid * 4 + 1] = ForwardRefReadOnlyInt(in x) * 1000 + ForwardInInt(in y);
        output[tid * 4 + 2] = ReadRefReadOnlyStruct(in s1) * 1000 + ReadInStruct(in s2);
        output[tid * 4 + 3] = ForwardRefReadOnlyAndInStruct(in s1, in s2);
    }

    [GpuKernel]
    public static void RValueArgumentsForReadOnlyRefKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        int x = input[tid];
        float f = x + 0.5f;
        RefTestStruct s = new RefTestStruct { Field = x, Value = 4 };
        RefTestStruct sCtor = new RefTestStruct(x, 4);

        output[tid * 6] = ReadInInt(5) * 1000 + ReadInInt(x + 1);
        output[tid * 6 + 1] = ReadInStruct(new RefTestStruct { Field = x, Value = 4 });
        output[tid * 6 + 2] = ForwardInInt(x + 2);
        output[tid * 6 + 3] = ReadInInt(ThreadIndex.X) * 10000 +
                              ReadInInt(BlockDimension.X) * 100 +
                              ReadInInt(input.Length);
        output[tid * 6 + 4] = s.PassComputedToIn * 10000 +
                              ReadInInt(s.ComputedSum) * 100 +
                              ReadInInt(ConstVal);
        output[tid * 6 + 5] = ReadInLong(x) * 1000000 +
                              ReadInLong(x + 1) * 10000 +
                              ReadInDouble(f) * 100 +
                              sCtor.Field * 10 +
                              sCtor.Value;
    }

    [GpuKernel]
    public static void KernelWithInParameters(in int scalar, in RefTestStruct s, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid * 3] = scalar + s.Field * 100 + s.Value;
        output[tid * 3 + 1] = ReadInInt(in scalar) * 1000 + ReadRefReadOnlyInt(in scalar);
        output[tid * 3 + 2] = ReadInStruct(in s) * 1000 + ReadRefReadOnlyStruct(in s);
    }

    [GpuKernel]
    public static void AutoPropertyInParameterSnapshotKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        int x = input[tid];
        AutoPropHolder h1 = new AutoPropHolder { AutoProp = x };
        AutoPropHolder h2 = new AutoPropHolder { NestedProp = new RefTestStruct { Field = x, Value = 4 } };
        int scalarSnap = MutateHolderAndReadInSnapshot(ref h1, h1.AutoProp);
        int structSnap = MutateHolderAndReadInStructSnapshot(ref h2, h2.NestedProp);
        output[tid * 4] = scalarSnap;
        output[tid * 4 + 1] = h1.AutoProp;
        output[tid * 4 + 2] = structSnap;
        output[tid * 4 + 3] = h2.NestedProp.Field * 10 + h2.NestedProp.Value;
    }
}
