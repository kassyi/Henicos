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

// The fields land at the same offsets on both sides; only the alignment of Float4 / Float2 / Vector2 differs on CUDA
public struct WideVectorsFirst
{
    public Float4 W;
    public Float2 V;
    public float A;
    public float B;
}

public struct SystemVectorFirst
{
    public Vector2 V;
    public float A;
    public float B;
}

// [field: FieldOffset] on auto-properties is read from the backing field
[StructLayout(LayoutKind.Explicit)]
public struct ExplicitAutoPropertyStruct
{
    [field: FieldOffset(0)]
    public int A { get; set; }

    [field: FieldOffset(4)]
    public float B { get; set; }
}

// The larger member goes first in the device union, so that value initialization zeroes all 8 bytes
[StructLayout(LayoutKind.Explicit)]
public struct IntLongUnion
{
    [FieldOffset(0)]
    public int I;

    [FieldOffset(0)]
    public long L;
}

// StructLayout.Size (#621): the CLR uses max(Size, end of the last field) without rounding up to the alignment; C++
// rounds sizeof up to the alignment, so a Size that is not a multiple of it gives a different stride
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct SizedPaddedInt
{
    public int A;
}

[StructLayout(LayoutKind.Sequential, Size = 6)]
public struct SizedOddInt
{
    public int A;
}

// Size smaller than the fields: the fields win (5 bytes on the host, 8 on the device)
[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct SizedBelowFields
{
    public int A;
    public byte B;
}

[StructLayout(LayoutKind.Explicit, Size = 6)]
public struct SizedExplicitOdd
{
    [FieldOffset(0)]
    public int I;
}

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 7)]
public struct SizedPacked
{
    public byte B;
    public int I;
}

// A sized struct as a field: the next field follows its 6 bytes on the host (B at 6, 8 bytes in all), its 8 bytes on
// the device
public struct SizedNestedOuter
{
    public SizedOddInt O;
    public byte B;
}

// 20 bytes on the host; CUDA aligns Vector4 to 16 bytes, so the device rounds Size up to 32
[StructLayout(LayoutKind.Sequential, Size = 20)]
public struct SizedVector4
{
    public Vector4 V;
}

// SizedGeneric<double>: 12 bytes on the host, 16 on the device (double is 8-byte aligned)
[StructLayout(LayoutKind.Sequential, Size = 12)]
public struct SizedGeneric<T> where T : unmanaged
{
    public T V;
}
