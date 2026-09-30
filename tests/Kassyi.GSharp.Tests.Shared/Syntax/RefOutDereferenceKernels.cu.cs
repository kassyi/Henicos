// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public struct RefTestStruct
{
    public int Field;
    public int Value;
}

public static partial class RefOutDereferenceKernels
{
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
}
