using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Layout;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct CharFieldRoundtripStruct
{
    public char Ch;
    public int Value;
}

[SkipSnapshotConsistency]
public static unsafe partial class StructLayoutParityKernels
{
    [GpuKernel]
    public static void MeasureSimpleSequentialKernel(ref SimpleSequential s, int[] outLayout)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            SimpleSequential local = default;
            SimpleSequential* p = &local;
            byte* basePtr = (byte*)p;
            outLayout[0] = (int)((byte*)(p + 1) - basePtr);
            outLayout[1] = (int)((byte*)&local.A - basePtr);
            outLayout[2] = (int)((byte*)&local.B - basePtr);
            outLayout[3] = (int)((byte*)&local.C - basePtr);

            s.A = (byte)(s.A + 1);
            s.B += 10;
            s.C += 1.5;
        }
    }

    [GpuKernel]
    public static void MeasureBoolAndCharSequentialKernel(ref BoolAndCharSequential s, int[] outLayout)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            BoolAndCharSequential local = default;
            BoolAndCharSequential* p = &local;
            byte* basePtr = (byte*)p;
            outLayout[0] = (int)((byte*)(p + 1) - basePtr);
            outLayout[1] = (int)((byte*)&local.Flag1 - basePtr);
            outLayout[2] = (int)((byte*)&local.Flag2 - basePtr);
            outLayout[3] = (int)((byte*)&local.Ch - basePtr);
            outLayout[4] = (int)((byte*)&local.Value - basePtr);

            s.Flag1 = !s.Flag1;
            s.Flag2 = !s.Flag2;
            s.Ch = (char)(s.Ch + 1);
            s.Value += 100;
        }
    }

    [GpuKernel]
    public static void RoundtripCharFieldByValueKernel(CharFieldRoundtripStruct input, CharFieldRoundtripStruct[] output)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            CharFieldRoundtripStruct result = default;
            result.Ch = (char)(input.Ch + 1);
            result.Value = input.Value + 42;
            output[0] = result;
        }
    }

    [GpuKernel]
    public static void RoundtripCharFieldArrayKernel(CharFieldRoundtripStruct[] items)
    {
        int idx = ThreadIndex.X;
        if (idx < items.Length)
        {
            CharFieldRoundtripStruct item = items[idx];
            item.Ch = (char)(item.Ch + 2);
            item.Value = item.Value * 2;
            items[idx] = item;
        }
    }

    [GpuKernel]
    public static void RoundtripBoolAndCharByValueKernel(BoolAndCharSequential input, BoolAndCharSequential[] output)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            BoolAndCharSequential result = default;
            result.Flag1 = !input.Flag1;
            result.Flag2 = !input.Flag2;
            result.Ch = (char)(input.Ch + 1);
            result.Value = input.Value + 42;
            output[0] = result;
        }
    }

    [GpuKernel]
    public static void RoundtripBoolAndCharArrayKernel(BoolAndCharSequential[] items)
    {
        int idx = ThreadIndex.X;
        if (idx < items.Length)
        {
            BoolAndCharSequential item = items[idx];
            item.Flag1 = !item.Flag1;
            item.Flag2 = !item.Flag2;
            item.Ch = (char)(item.Ch + 2);
            item.Value = item.Value * 2;
            items[idx] = item;
        }
    }

    [GpuKernel]
    public static void MeasureNestedOuterKernel(ref NestedOuterStruct s, int[] outLayout)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            NestedInnerStruct innerLocal = default;
            NestedInnerStruct* pInner = &innerLocal;
            byte* innerBase = (byte*)pInner;
            outLayout[0] = (int)((byte*)(pInner + 1) - innerBase);
            outLayout[1] = (int)((byte*)&innerLocal.A - innerBase);
            outLayout[2] = (int)((byte*)&innerLocal.B - innerBase);

            NestedOuterStruct outerLocal = default;
            NestedOuterStruct* pOuter = &outerLocal;
            byte* outerBase = (byte*)pOuter;
            outLayout[3] = (int)((byte*)(pOuter + 1) - outerBase);
            outLayout[4] = (int)((byte*)&outerLocal.Prefix - outerBase);
            outLayout[5] = (int)((byte*)&outerLocal.Inner - outerBase);
            outLayout[6] = (int)((byte*)&outerLocal.Suffix - outerBase);

            s.Prefix = (byte)(s.Prefix + 1);
            s.Inner.A = (byte)(s.Inner.A + 2);
            s.Inner.B += 1000L;
            s.Suffix = (short)(s.Suffix + 3);
        }
    }

    [GpuKernel]
    public static void MeasureSystemNumericsVectorsKernel(ref SystemNumericsVectorsStruct s, int[] outLayout)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            SystemNumericsVectorsStruct local = default;
            SystemNumericsVectorsStruct* p = &local;
            byte* basePtr = (byte*)p;
            outLayout[0] = (int)((byte*)(p + 1) - basePtr);
            outLayout[1] = (int)((byte*)&local.Tag1 - basePtr);
            outLayout[2] = (int)((byte*)&local.V2 - basePtr);
            outLayout[3] = (int)((byte*)&local.Tag2 - basePtr);
            outLayout[4] = (int)((byte*)&local.V3 - basePtr);
            outLayout[5] = (int)((byte*)&local.Tag3 - basePtr);
            outLayout[6] = (int)((byte*)&local.V4 - basePtr);

            s.Tag1 = (byte)(s.Tag1 + 1);
            s.V2 = new System.Numerics.Vector2(s.V2.X + 1.0f, s.V2.Y + 2.0f);
            s.Tag2 = (byte)(s.Tag2 + 1);
            s.V3 = new System.Numerics.Vector3(s.V3.X + 3.0f, s.V3.Y + 4.0f, s.V3.Z + 5.0f);
            s.Tag3 = (byte)(s.Tag3 + 1);
            s.V4 = new System.Numerics.Vector4(s.V4.X + 6.0f, s.V4.Y + 7.0f, s.V4.Z + 8.0f, s.V4.W + 9.0f);
        }
    }

    [GpuKernel]
    public static void MeasureCoreVectorsFloatIntUIntKernel(ref CoreVectorsFloatIntUIntStruct s, int[] outLayout)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            CoreVectorsFloatIntUIntStruct local = default;
            CoreVectorsFloatIntUIntStruct* p = &local;
            byte* basePtr = (byte*)p;
            outLayout[0] = (int)((byte*)(p + 1) - basePtr);
            outLayout[1] = (int)((byte*)&local.Tag1 - basePtr);
            outLayout[2] = (int)((byte*)&local.F2 - basePtr);
            outLayout[3] = (int)((byte*)&local.F3 - basePtr);
            outLayout[4] = (int)((byte*)&local.F4 - basePtr);
            outLayout[5] = (int)((byte*)&local.Tag2 - basePtr);
            outLayout[6] = (int)((byte*)&local.I2 - basePtr);
            outLayout[7] = (int)((byte*)&local.I3 - basePtr);
            outLayout[8] = (int)((byte*)&local.I4 - basePtr);
            outLayout[9] = (int)((byte*)&local.Tag3 - basePtr);
            outLayout[10] = (int)((byte*)&local.U2 - basePtr);
            outLayout[11] = (int)((byte*)&local.U3 - basePtr);
            outLayout[12] = (int)((byte*)&local.U4 - basePtr);

            s.Tag1 = (byte)(s.Tag1 + 1);
            s.F2 = new Float2(s.F2.X + 1.0f, s.F2.Y + 2.0f);
            s.F4 = new Float4(s.F4.X + 1.0f, s.F4.Y + 2.0f, s.F4.Z + 3.0f, s.F4.W + 4.0f);
            s.Tag2 = (byte)(s.Tag2 + 1);
            s.I2 = new Int2(s.I2.X + 10, s.I2.Y + 20);
            s.I4 = new Int4(s.I4.X + 10, s.I4.Y + 20, s.I4.Z + 30, s.I4.W + 40);
            s.Tag3 = (byte)(s.Tag3 + 1);
            s.U2 = new UInt2(s.U2.X + 100u, s.U2.Y + 200u);
            s.U4 = new UInt4(s.U4.X + 100u, s.U4.Y + 200u, s.U4.Z + 300u, s.U4.W + 400u);
        }
    }

    [GpuKernel]
    public static void MeasureCoreVectors64BitKernel(ref CoreVectors64BitStruct s, int[] outLayout)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            CoreVectors64BitStruct local = default;
            CoreVectors64BitStruct* p = &local;
            byte* basePtr = (byte*)p;
            outLayout[0] = (int)((byte*)(p + 1) - basePtr);
            outLayout[1] = (int)((byte*)&local.Tag1 - basePtr);
            outLayout[2] = (int)((byte*)&local.D2 - basePtr);
            outLayout[3] = (int)((byte*)&local.D3 - basePtr);
            outLayout[4] = (int)((byte*)&local.D4 - basePtr);
            outLayout[5] = (int)((byte*)&local.Tag2 - basePtr);
            outLayout[6] = (int)((byte*)&local.L2 - basePtr);
            outLayout[7] = (int)((byte*)&local.L3 - basePtr);
            outLayout[8] = (int)((byte*)&local.L4 - basePtr);
            outLayout[9] = (int)((byte*)&local.Tag3 - basePtr);
            outLayout[10] = (int)((byte*)&local.UL2 - basePtr);
            outLayout[11] = (int)((byte*)&local.UL3 - basePtr);
            outLayout[12] = (int)((byte*)&local.UL4 - basePtr);

            s.Tag1 = (byte)(s.Tag1 + 1);
            s.D2 = new Double2(s.D2.X + 1.0, s.D2.Y + 2.0);
            s.D4 = new Double4(s.D4.X + 1.0, s.D4.Y + 2.0, s.D4.Z + 3.0, s.D4.W + 4.0);
            s.Tag2 = (byte)(s.Tag2 + 1);
            s.L2 = new Long2(s.L2.X + 10L, s.L2.Y + 20L);
            s.L4 = new Long4(s.L4.X + 10L, s.L4.Y + 20L, s.L4.Z + 30L, s.L4.W + 40L);
            s.Tag3 = (byte)(s.Tag3 + 1);
            s.UL2 = new ULong2(s.UL2.X + 100UL, s.UL2.Y + 200UL);
            s.UL4 = new ULong4(s.UL4.X + 100UL, s.UL4.Y + 200UL, s.UL4.Z + 300UL, s.UL4.W + 400UL);
        }
    }
}
