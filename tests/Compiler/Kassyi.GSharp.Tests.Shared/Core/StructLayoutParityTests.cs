using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.TestCases.Layout;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

[TestTarget(ExecutionTarget.GpuExecution)]
[Trait("Category", "GpuExecution")]
public class StructLayoutParityTests
{
    private static Type GetShadowType(string structName)
    {
        var aotContexts = typeof(StructLayoutParityKernels).GetNestedType("AotContexts");
        Assert.NotNull(aotContexts);
        var shadowType = aotContexts.GetNestedType(structName);
        Assert.NotNull(shadowType);
        return shadowType;
    }

    private static void AssertShadowParity(string structName, int[] deviceLayout, params string[] fieldNames)
    {
        var shadowType = GetShadowType(structName);
        Assert.Equal(deviceLayout[0], Marshal.SizeOf(shadowType));
        for (int i = 0; i < fieldNames.Length; i++)
        {
            Assert.Equal(deviceLayout[i + 1], Marshal.OffsetOf(shadowType, fieldNames[i]).ToInt32());
        }
    }

    private static int HostOffset<TStruct, TField>(ref TStruct s, ref TField field)
        where TStruct : unmanaged
        where TField : unmanaged
    {
        return (int)Unsafe.ByteOffset(ref Unsafe.As<TStruct, byte>(ref s), ref Unsafe.As<TField, byte>(ref field));
    }

    [Fact]
    public void SimpleSequential_DeviceShadowAndHostLayout_MatchAndRoundtrip()
    {
        var s = new SimpleSequential { A = 7, B = 100, C = 2.5 };
        var layout = new int[4];

        StructLayoutParityKernels.LaunchMeasureSimpleSequentialKernel(ref s, layout);

        AssertShadowParity(nameof(SimpleSequential), layout,
            nameof(SimpleSequential.A),
            nameof(SimpleSequential.B),
            nameof(SimpleSequential.C));

        Assert.Equal(Unsafe.SizeOf<SimpleSequential>(), layout[0]);
        Assert.Equal(HostOffset(ref s, ref s.A), layout[1]);
        Assert.Equal(HostOffset(ref s, ref s.B), layout[2]);
        Assert.Equal(HostOffset(ref s, ref s.C), layout[3]);

        Assert.Equal((byte)8, s.A);
        Assert.Equal(110, s.B);
        Assert.Equal(4.0, s.C);
    }

    [Fact]
    public void BoolAndCharSequential_DeviceShadowAndHostLayout_MatchAndRoundtrip()
    {
        var s = new BoolAndCharSequential { Flag1 = true, Flag2 = false, Ch = 'A', Value = 50 };
        var layout = new int[5];

        StructLayoutParityKernels.LaunchMeasureBoolAndCharSequentialKernel(ref s, layout);

        AssertShadowParity(nameof(BoolAndCharSequential), layout,
            nameof(BoolAndCharSequential.Flag1),
            nameof(BoolAndCharSequential.Flag2),
            nameof(BoolAndCharSequential.Ch),
            nameof(BoolAndCharSequential.Value));

        Assert.Equal(Unsafe.SizeOf<BoolAndCharSequential>(), layout[0]);
        Assert.Equal(HostOffset(ref s, ref s.Flag1), layout[1]);
        Assert.Equal(HostOffset(ref s, ref s.Flag2), layout[2]);
        Assert.Equal(HostOffset(ref s, ref s.Ch), layout[3]);
        Assert.Equal(HostOffset(ref s, ref s.Value), layout[4]);

        Assert.False(s.Flag1);
        Assert.True(s.Flag2);
        Assert.Equal('B', s.Ch);
        Assert.Equal(150, s.Value);
    }

    [Fact]
    public void CharFieldStruct_ByValueAndArray_Roundtrip()
    {
        var input = new CharFieldRoundtripStruct { Ch = '\u3042', Value = 10 };
        var singleOut = new CharFieldRoundtripStruct[1];

        StructLayoutParityKernels.LaunchRoundtripCharFieldByValueKernel(input, singleOut);

        Assert.Equal((char)('\u3042' + 1), singleOut[0].Ch);
        Assert.Equal(52, singleOut[0].Value);

        var charItems = new[]
        {
            new CharFieldRoundtripStruct { Ch = 'X', Value = 7 },
            new CharFieldRoundtripStruct { Ch = '\u03A9', Value = -15 }
        };

        StructLayoutParityKernels.LaunchRoundtripCharFieldArrayKernel(charItems, blockDimX: 2);

        Assert.Equal('Z', charItems[0].Ch);
        Assert.Equal(14, charItems[0].Value);
        Assert.Equal((char)('\u03A9' + 2), charItems[1].Ch);
        Assert.Equal(-30, charItems[1].Value);

#if !GSHARP_CPU
        var boolCharInput = new BoolAndCharSequential { Flag1 = false, Flag2 = true, Ch = '\u3042', Value = 10 };
        var boolCharOut = new BoolAndCharSequential[1];
        StructLayoutParityKernels.LaunchRoundtripBoolAndCharByValueKernel(boolCharInput, boolCharOut);
        Assert.True(boolCharOut[0].Flag1);
        Assert.False(boolCharOut[0].Flag2);
        Assert.Equal((char)('\u3042' + 1), boolCharOut[0].Ch);
        Assert.Equal(52, boolCharOut[0].Value);
#endif

        var boolCharItems = new[]
        {
            new BoolAndCharSequential { Flag1 = true, Flag2 = false, Ch = 'X', Value = 7 },
            new BoolAndCharSequential { Flag1 = false, Flag2 = true, Ch = '\u03A9', Value = -15 }
        };

        StructLayoutParityKernels.LaunchRoundtripBoolAndCharArrayKernel(boolCharItems, blockDimX: 2);

        Assert.False(boolCharItems[0].Flag1);
        Assert.True(boolCharItems[0].Flag2);
        Assert.Equal('Z', boolCharItems[0].Ch);
        Assert.Equal(14, boolCharItems[0].Value);

        Assert.True(boolCharItems[1].Flag1);
        Assert.False(boolCharItems[1].Flag2);
        Assert.Equal((char)('\u03A9' + 2), boolCharItems[1].Ch);
        Assert.Equal(-30, boolCharItems[1].Value);
    }

    [Fact]
    public void NestedOuterStruct_DeviceShadowAndHostLayout_MatchAndRoundtrip()
    {
        var s = new NestedOuterStruct
        {
            Prefix = 10,
            Inner = new NestedInnerStruct { A = 20, B = 3000L },
            Suffix = 40
        };
        var layout = new int[7];

        StructLayoutParityKernels.LaunchMeasureNestedOuterKernel(ref s, layout);

        var innerLayout = new[] { layout[0], layout[1], layout[2] };
        AssertShadowParity(nameof(NestedInnerStruct), innerLayout,
            nameof(NestedInnerStruct.A),
            nameof(NestedInnerStruct.B));

        var outerLayout = new[] { layout[3], layout[4], layout[5], layout[6] };
        AssertShadowParity(nameof(NestedOuterStruct), outerLayout,
            nameof(NestedOuterStruct.Prefix),
            nameof(NestedOuterStruct.Inner),
            nameof(NestedOuterStruct.Suffix));

        var inner = s.Inner;
        Assert.Equal(Unsafe.SizeOf<NestedInnerStruct>(), layout[0]);
        Assert.Equal(HostOffset(ref inner, ref inner.A), layout[1]);
        Assert.Equal(HostOffset(ref inner, ref inner.B), layout[2]);

        Assert.Equal(Unsafe.SizeOf<NestedOuterStruct>(), layout[3]);
        Assert.Equal(HostOffset(ref s, ref s.Prefix), layout[4]);
        Assert.Equal(HostOffset(ref s, ref s.Inner), layout[5]);
        Assert.Equal(HostOffset(ref s, ref s.Suffix), layout[6]);

        Assert.Equal((byte)11, s.Prefix);
        Assert.Equal((byte)22, s.Inner.A);
        Assert.Equal(4000L, s.Inner.B);
        Assert.Equal((short)43, s.Suffix);
    }

    [Fact]
    public void SystemNumericsVectorsStruct_DeviceShadowLayout_MatchesAndRoundtrips()
    {
        var s = new SystemNumericsVectorsStruct
        {
            Tag1 = 1,
            V2 = new Vector2(10.0f, 20.0f),
            Tag2 = 2,
            V3 = new Vector3(30.0f, 40.0f, 50.0f),
            Tag3 = 3,
            V4 = new Vector4(60.0f, 70.0f, 80.0f, 90.0f)
        };
        var layout = new int[7];

        StructLayoutParityKernels.LaunchMeasureSystemNumericsVectorsKernel(ref s, layout);

        AssertShadowParity(nameof(SystemNumericsVectorsStruct), layout,
            nameof(SystemNumericsVectorsStruct.Tag1),
            nameof(SystemNumericsVectorsStruct.V2),
            nameof(SystemNumericsVectorsStruct.Tag2),
            nameof(SystemNumericsVectorsStruct.V3),
            nameof(SystemNumericsVectorsStruct.Tag3),
            nameof(SystemNumericsVectorsStruct.V4));

#if GSHARP_CPU
        Assert.Equal(Unsafe.SizeOf<SystemNumericsVectorsStruct>(), layout[0]);
        Assert.Equal(HostOffset(ref s, ref s.Tag1), layout[1]);
        Assert.Equal(HostOffset(ref s, ref s.V2), layout[2]);
        Assert.Equal(HostOffset(ref s, ref s.Tag2), layout[3]);
        Assert.Equal(HostOffset(ref s, ref s.V3), layout[4]);
        Assert.Equal(HostOffset(ref s, ref s.Tag3), layout[5]);
        Assert.Equal(HostOffset(ref s, ref s.V4), layout[6]);
#else
        Assert.True(layout[0] > Unsafe.SizeOf<SystemNumericsVectorsStruct>());
#endif

        Assert.Equal((byte)2, s.Tag1);
        Assert.Equal(new Vector2(11.0f, 22.0f), s.V2);
        Assert.Equal((byte)3, s.Tag2);
        Assert.Equal(new Vector3(33.0f, 44.0f, 55.0f), s.V3);
        Assert.Equal((byte)4, s.Tag3);
        Assert.Equal(new Vector4(66.0f, 77.0f, 88.0f, 99.0f), s.V4);
    }

    [Fact]
    public void CoreVectorsFloatIntUIntStruct_DeviceShadowLayout_MatchesAndRoundtrips()
    {
        var s = new CoreVectorsFloatIntUIntStruct
        {
            Tag1 = 10,
            F2 = new Float2(1.0f, 2.0f),
            F3 = new Float3(3.0f, 4.0f, 5.0f),
            F4 = new Float4(6.0f, 7.0f, 8.0f, 9.0f),
            Tag2 = 20,
            I2 = new Int2(1, 2),
            I3 = new Int3(3, 4, 5),
            I4 = new Int4(6, 7, 8, 9),
            Tag3 = 30,
            U2 = new UInt2(10u, 20u),
            U3 = new UInt3(30u, 40u, 50u),
            U4 = new UInt4(60u, 70u, 80u, 90u)
        };
        var layout = new int[13];

        StructLayoutParityKernels.LaunchMeasureCoreVectorsFloatIntUIntKernel(ref s, layout);

        AssertShadowParity(nameof(CoreVectorsFloatIntUIntStruct), layout,
            nameof(CoreVectorsFloatIntUIntStruct.Tag1),
            nameof(CoreVectorsFloatIntUIntStruct.F2),
            nameof(CoreVectorsFloatIntUIntStruct.F3),
            nameof(CoreVectorsFloatIntUIntStruct.F4),
            nameof(CoreVectorsFloatIntUIntStruct.Tag2),
            nameof(CoreVectorsFloatIntUIntStruct.I2),
            nameof(CoreVectorsFloatIntUIntStruct.I3),
            nameof(CoreVectorsFloatIntUIntStruct.I4),
            nameof(CoreVectorsFloatIntUIntStruct.Tag3),
            nameof(CoreVectorsFloatIntUIntStruct.U2),
            nameof(CoreVectorsFloatIntUIntStruct.U3),
            nameof(CoreVectorsFloatIntUIntStruct.U4));

#if GSHARP_CPU
        Assert.Equal(Unsafe.SizeOf<CoreVectorsFloatIntUIntStruct>(), layout[0]);
        Assert.Equal(HostOffset(ref s, ref s.Tag1), layout[1]);
        Assert.Equal(HostOffset(ref s, ref s.F2), layout[2]);
        Assert.Equal(HostOffset(ref s, ref s.F3), layout[3]);
        Assert.Equal(HostOffset(ref s, ref s.F4), layout[4]);
        Assert.Equal(HostOffset(ref s, ref s.Tag2), layout[5]);
        Assert.Equal(HostOffset(ref s, ref s.I2), layout[6]);
        Assert.Equal(HostOffset(ref s, ref s.I3), layout[7]);
        Assert.Equal(HostOffset(ref s, ref s.I4), layout[8]);
        Assert.Equal(HostOffset(ref s, ref s.Tag3), layout[9]);
        Assert.Equal(HostOffset(ref s, ref s.U2), layout[10]);
        Assert.Equal(HostOffset(ref s, ref s.U3), layout[11]);
        Assert.Equal(HostOffset(ref s, ref s.U4), layout[12]);
#else
        Assert.True(layout[0] > Unsafe.SizeOf<CoreVectorsFloatIntUIntStruct>());
#endif

        Assert.Equal((byte)11, s.Tag1);
        Assert.Equal(2.0f, s.F2.X);
        Assert.Equal(4.0f, s.F2.Y);
        Assert.Equal(7.0f, s.F4.X);
        Assert.Equal(13.0f, s.F4.W);
        Assert.Equal((byte)21, s.Tag2);
        Assert.Equal(11, s.I2.X);
        Assert.Equal(22, s.I2.Y);
        Assert.Equal(16, s.I4.X);
        Assert.Equal(49, s.I4.W);
        Assert.Equal((byte)31, s.Tag3);
        Assert.Equal(110u, s.U2.X);
        Assert.Equal(220u, s.U2.Y);
        Assert.Equal(160u, s.U4.X);
        Assert.Equal(490u, s.U4.W);
    }

    [Fact]
    public void CoreVectors64BitStruct_DeviceShadowLayout_MatchesAndRoundtrips()
    {
        var s = new CoreVectors64BitStruct
        {
            Tag1 = 1,
            D2 = new Double2(1.0, 2.0),
            D3 = new Double3(3.0, 4.0, 5.0),
            D4 = new Double4(6.0, 7.0, 8.0, 9.0),
            Tag2 = 2,
            L2 = new Long2(10L, 20L),
            L3 = new Long3(30L, 40L, 50L),
            L4 = new Long4(60L, 70L, 80L, 90L),
            Tag3 = 3,
            UL2 = new ULong2(100UL, 200UL),
            UL3 = new ULong3(300UL, 400UL, 500UL),
            UL4 = new ULong4(600UL, 700UL, 800UL, 900UL)
        };
        var layout = new int[13];

        StructLayoutParityKernels.LaunchMeasureCoreVectors64BitKernel(ref s, layout);

        AssertShadowParity(nameof(CoreVectors64BitStruct), layout,
            nameof(CoreVectors64BitStruct.Tag1),
            nameof(CoreVectors64BitStruct.D2),
            nameof(CoreVectors64BitStruct.D3),
            nameof(CoreVectors64BitStruct.D4),
            nameof(CoreVectors64BitStruct.Tag2),
            nameof(CoreVectors64BitStruct.L2),
            nameof(CoreVectors64BitStruct.L3),
            nameof(CoreVectors64BitStruct.L4),
            nameof(CoreVectors64BitStruct.Tag3),
            nameof(CoreVectors64BitStruct.UL2),
            nameof(CoreVectors64BitStruct.UL3),
            nameof(CoreVectors64BitStruct.UL4));

#if GSHARP_CPU
        Assert.Equal(Unsafe.SizeOf<CoreVectors64BitStruct>(), layout[0]);
        Assert.Equal(HostOffset(ref s, ref s.Tag1), layout[1]);
        Assert.Equal(HostOffset(ref s, ref s.D2), layout[2]);
        Assert.Equal(HostOffset(ref s, ref s.D3), layout[3]);
        Assert.Equal(HostOffset(ref s, ref s.D4), layout[4]);
        Assert.Equal(HostOffset(ref s, ref s.Tag2), layout[5]);
        Assert.Equal(HostOffset(ref s, ref s.L2), layout[6]);
        Assert.Equal(HostOffset(ref s, ref s.L3), layout[7]);
        Assert.Equal(HostOffset(ref s, ref s.L4), layout[8]);
        Assert.Equal(HostOffset(ref s, ref s.Tag3), layout[9]);
        Assert.Equal(HostOffset(ref s, ref s.UL2), layout[10]);
        Assert.Equal(HostOffset(ref s, ref s.UL3), layout[11]);
        Assert.Equal(HostOffset(ref s, ref s.UL4), layout[12]);
#else
        Assert.True(layout[0] > Unsafe.SizeOf<CoreVectors64BitStruct>());
#endif

        Assert.Equal((byte)2, s.Tag1);
        Assert.Equal(2.0, s.D2.X);
        Assert.Equal(4.0, s.D2.Y);
        Assert.Equal(7.0, s.D4.X);
        Assert.Equal(13.0, s.D4.W);
        Assert.Equal((byte)3, s.Tag2);
        Assert.Equal(20L, s.L2.X);
        Assert.Equal(40L, s.L2.Y);
        Assert.Equal(70L, s.L4.X);
        Assert.Equal(130L, s.L4.W);
        Assert.Equal((byte)4, s.Tag3);
        Assert.Equal(200UL, s.UL2.X);
        Assert.Equal(400UL, s.UL2.Y);
        Assert.Equal(700UL, s.UL4.X);
        Assert.Equal(1300UL, s.UL4.W);
    }

    [Fact]
    public void ExplicitUnion_DeviceShadowAndHostLayout_MatchAndRoundtrip()
    {
        var s = new ExplicitUnionStruct { AsFloat = 1.5f, Tail = 9 };
        var layout = new int[4];

        StructLayoutParityKernels.LaunchMeasureExplicitUnionKernel(ref s, layout);

        AssertShadowParity(nameof(ExplicitUnionStruct), layout,
            nameof(ExplicitUnionStruct.AsInt),
            nameof(ExplicitUnionStruct.AsFloat),
            nameof(ExplicitUnionStruct.Tail));

        Assert.Equal(Unsafe.SizeOf<ExplicitUnionStruct>(), layout[0]);
        Assert.Equal(HostOffset(ref s, ref s.AsInt), layout[1]);
        Assert.Equal(HostOffset(ref s, ref s.AsFloat), layout[2]);
        Assert.Equal(HostOffset(ref s, ref s.Tail), layout[3]);

        Assert.Equal(3f, s.AsFloat);
        Assert.Equal(BitConverter.SingleToInt32Bits(3f), s.AsInt);
        Assert.Equal((byte)10, s.Tail);
    }

    [Fact]
    public void ExplicitGapWithSize_DeviceShadowAndHostLayout_MatchAndArrayRoundtrips()
    {
        var items = new[]
        {
            new ExplicitGapWithSizeStruct { First = 1, Middle = 2, Last = 3 },
            new ExplicitGapWithSizeStruct { First = 4, Middle = 5, Last = 6 }
        };
        var probe = new ExplicitGapWithSizeStruct { First = 7, Middle = 8, Last = 9 };
        var layout = new int[4];

        StructLayoutParityKernels.LaunchMeasureExplicitGapArrayKernel(ref probe, items, layout, blockDimX: 2);

        AssertShadowParity(nameof(ExplicitGapWithSizeStruct), layout,
            nameof(ExplicitGapWithSizeStruct.First),
            nameof(ExplicitGapWithSizeStruct.Middle),
            nameof(ExplicitGapWithSizeStruct.Last));

        var s = items[0];
        Assert.Equal(Unsafe.SizeOf<ExplicitGapWithSizeStruct>(), layout[0]);
        Assert.Equal(HostOffset(ref s, ref s.First), layout[1]);
        Assert.Equal(HostOffset(ref s, ref s.Middle), layout[2]);
        Assert.Equal(HostOffset(ref s, ref s.Last), layout[3]);

        Assert.Equal(9, probe.Middle);
        Assert.Equal((short)9, probe.Last);
        Assert.Equal((byte)2, items[0].First);
        Assert.Equal(20, items[0].Middle);
        Assert.Equal((short)2, items[0].Last);
        Assert.Equal((byte)5, items[1].First);
        Assert.Equal(50, items[1].Middle);
        Assert.Equal((short)5, items[1].Last);
    }

    // CPU passes the struct in its managed layout until L4
#if GSHARP_CPU
    [Fact(Skip = "L4")]
#else
    [Fact]
#endif
    public void InlineArrayOfArrays_ByRef_CopiesEachElementArray()
    {
        var h = new ArrayFieldPairHolder { Tag = 3 };
        h.Arrays[0] = [10, 20];
        h.Arrays[1] = [30, 40, 50];
        var o = new int[3];

        StructLayoutParityKernels.LaunchInlineArrayOfArraysKernel(ref h, o);

        Assert.Equal([3, 20, 3], o);
        Assert.Equal(77, h.Arrays[1][0]);
        Assert.Equal([10, 20], h.Arrays[0]);
    }

    [Fact]
    public void ExplicitAutoProperties_LocalAndArgument_KeepSeparateFields()
    {
        var input = new ExplicitAutoPropertyStruct { A = 11, B = 4.5f };
        var o = new int[3];
        var f = new float[2];

        StructLayoutParityKernels.LaunchExplicitAutoPropertyKernel(input, o, f);

        Assert.Equal([7, 11, Unsafe.SizeOf<ExplicitAutoPropertyStruct>()], o);
        Assert.Equal([2.5f, 4.5f], f);
    }

    // An auxiliary check: the stack is often zero already, so it stays green on the CPU even when the union order is
    // wrong. ExplicitUnionEmissionTests pins the order itself.
    [Fact]
    public void IntLongUnion_ValueInitialization_ZeroesTheWholeUnion()
    {
        var o = new long[2];

        StructLayoutParityKernels.LaunchIntLongUnionKernel(o);

        Assert.Equal([0L, -1L], o);
    }

    [Fact]
    public void ExplicitPacked_DeviceShadowAndHostLayout_MatchAndRoundtrip()
    {
        var s = new ExplicitPackedStruct { Head = 3, Value = 1.25 };
        var layout = new int[3];

        StructLayoutParityKernels.LaunchMeasureExplicitPackedKernel(ref s, layout);

        AssertShadowParity(nameof(ExplicitPackedStruct), layout,
            nameof(ExplicitPackedStruct.Head),
            nameof(ExplicitPackedStruct.Value));

        Assert.Equal(Unsafe.SizeOf<ExplicitPackedStruct>(), layout[0]);
        Assert.Equal(HostOffset(ref s, ref s.Head), layout[1]);
        Assert.Equal(HostOffset(ref s, ref s.Value), layout[2]);
        Assert.Equal((byte)4, s.Head);
        Assert.Equal(2.5, s.Value);
    }
}
