using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Layout;

public enum LayoutByteEnum : byte
{
    Zero = 0,
    One = 1
}

public enum LayoutLongEnum : long
{
    Zero = 0,
    One = 1
}

public struct SimpleSequential
{
    public byte A;
    public int B;
    public double C;
}

public struct BoolAndCharSequential
{
    public bool Flag1;
    public bool Flag2;
    public char Ch;
    public int Value;
}

public struct EnumAndPointerSequential
{
    public LayoutByteEnum ByteEnum;
    public unsafe int* Ptr;
    public LayoutLongEnum LongEnum;
    public nint NativeInt;
    public nuint NativeUInt;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Packed1Struct
{
    public byte A;
    public double B;
    public int C;
    public short D;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Packed2Struct
{
    public byte A;
    public int B;
    public long C;
    public byte D;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Packed4Struct
{
    public byte A;
    public double B;
    public int C;
}

[StructLayout(LayoutKind.Explicit)]
public struct ExplicitUnionStruct
{
    [FieldOffset(0)]
    public int AsInt;

    [FieldOffset(0)]
    public float AsFloat;

    [FieldOffset(4)]
    public byte Tail;
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct ExplicitGapWithSizeStruct
{
    [FieldOffset(0)]
    public byte First;

    [FieldOffset(8)]
    public int Middle;

    [FieldOffset(16)]
    public short Last;
}

[StructLayout(LayoutKind.Explicit, Pack = 2)]
public struct ExplicitPackedStruct
{
    [FieldOffset(0)]
    public byte Head;

    [FieldOffset(2)]
    public double Value;
}

[StructLayout(LayoutKind.Auto)]
public struct AutoLayoutCase
{
    public byte A;
    public long B;
    public byte C;
}

public struct DirectArrayFieldStruct
{
    public int Scalar;
    public int[] Values;
}

public struct NestedArrayFieldStruct
{
    public byte Header;
    public DirectArrayFieldStruct Inner;
}

[InlineArray(3)]
public struct InlineArrayWithArrayElement
{
    private DirectArrayFieldStruct _element0;
}

public struct OuterWithInlineArrayOfArrayElement
{
    public int Tag;
    public InlineArrayWithArrayElement Items;
}

public struct NestedInnerStruct
{
    public byte A;
    public long B;
}

public struct NestedOuterStruct
{
    public byte Prefix;
    public NestedInnerStruct Inner;
    public short Suffix;
}

[InlineArray(4)]
public struct InlineFourInts
{
    private int _element0;
}

public struct StructWithInlineFourInts
{
    public byte Tag;
    public InlineFourInts Values;
    public byte Tail;
}

[InlineArray(3)]
public struct InlineThreeFloat2
{
    private Float2 _element0;
}

public struct DivergentInlineElem
{
    public byte Tag;
    public Float2 Vec;
}

[InlineArray(2)]
public struct InlineTwoDivergentElems
{
    private DivergentInlineElem _element0;
}

public struct StructWithDivergentInlineArray
{
    public InlineTwoDivergentElems Items;
    public int Tail;
}

public unsafe struct FixedBufferStruct
{
    public byte Header;
    public fixed int Data[5];
    public short Footer;
}

public struct GenericBox<T> where T : unmanaged
{
    public byte Tag;
    public T Value;
    public byte Tail;
}

public struct GenericBoxHolder
{
    public GenericBox<int> IntBox;
    public GenericBox<double> DoubleBox;
    public GenericBox<Float4> Float4Box;
}

public struct NullableFieldsStruct
{
    public byte Prefix;
    public int? NullableInt;
    public double? NullableDouble;
    public Float2? NullableFloat2;
}

public struct SystemNumericsVectorsStruct
{
    public byte Tag1;
    public Vector2 V2;
    public byte Tag2;
    public Vector3 V3;
    public byte Tag3;
    public Vector4 V4;
}

public struct CoreVectorsFloatIntUIntStruct
{
    public byte Tag1;
    public Float2 F2;
    public Float3 F3;
    public Float4 F4;
    public byte Tag2;
    public Int2 I2;
    public Int3 I3;
    public Int4 I4;
    public byte Tag3;
    public UInt2 U2;
    public UInt3 U3;
    public UInt4 U4;
}

public struct CoreVectors64BitStruct
{
    public byte Tag1;
    public Double2 D2;
    public Double3 D3;
    public Double4 D4;
    public byte Tag2;
    public Long2 L2;
    public Long3 L3;
    public Long4 L4;
    public byte Tag3;
    public ULong2 UL2;
    public ULong3 UL3;
    public ULong4 UL4;
}

public struct EmptyUserStruct
{
}
