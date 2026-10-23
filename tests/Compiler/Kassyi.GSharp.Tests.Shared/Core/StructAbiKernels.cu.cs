using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AbiPackedStruct
{
    public byte A;
    public double B;
    public int C;
}

[InlineArray(4)]
public struct AbiFourInts
{
    public int Element;
}

public struct AbiInlineArrayStruct
{
    public byte A;
    public AbiFourInts B;
    public int C;
}

public struct AbiCoreFieldStruct
{
    public byte A;
    public Float3 B;
    public int C;
}

// Private element, as a real [InlineArray] struct declares it (#468).
[InlineArray(4)]
public struct AbiPrivateFourInts
{
    private int _element0;
}

public struct AbiPrivateInlineArrayStruct
{
    public byte A;
    public AbiPrivateFourInts B;
    public int C;
}

public unsafe struct AbiFixedBufferStruct
{
    public byte A;
    public fixed int B[4];
    public int C;
}

public struct AbiAutoPropertyStruct
{
    public byte A;
    public double B { get; set; }
    public int C;
}

// A computed property has no backing field, so it is no part of the layout on either side (#471). The device reads it
// through a getter; each form of getter body is here, and two of them read another computed property.
public struct AbiComputedPropertyStruct
{
    public byte A;
    public int Sum => A + 1;
    public int C;
    public int Twice => Sum * 2;
    public int Product { get => A * C; }

    public int Difference
    {
        get { return this.C - this.Sum; }
    }
}

// Each Measure kernel writes the device offset of C and the device size of the struct. A Roundtrip kernel takes the
// struct by ref, which makes the generator emit its native shadow struct (AotContexts.*), and changes A and C.
public static partial class StructAbiKernels
{
    [GpuKernel]
    public static void MeasurePackedKernel(int[] layout)
    {
        unsafe
        {
            AbiPackedStruct s = default;
            AbiPackedStruct* p = &s;
            layout[0] = (int)((byte*)&s.C - (byte*)p);
            layout[1] = (int)((byte*)(p + 1) - (byte*)p);
        }
    }

    [GpuKernel]
    public static void RoundtripPackedKernel(ref AbiPackedStruct data)
    {
        data.A = (byte)(data.A + 1);
        data.C = data.C + 1;
    }

    [GpuKernel]
    public static void MeasureInlineArrayKernel(int[] layout)
    {
        unsafe
        {
            AbiInlineArrayStruct s = default;
            AbiInlineArrayStruct* p = &s;
            layout[0] = (int)((byte*)&s.C - (byte*)p);
            layout[1] = (int)((byte*)(p + 1) - (byte*)p);
        }
    }

    [GpuKernel]
    public static void RoundtripInlineArrayKernel(ref AbiInlineArrayStruct data)
    {
        data.A = (byte)(data.A + 1);
        data.C = data.C + 1;
    }

    [GpuKernel]
    public static void MeasureCoreFieldKernel(int[] layout)
    {
        unsafe
        {
            AbiCoreFieldStruct s = default;
            AbiCoreFieldStruct* p = &s;
            layout[0] = (int)((byte*)&s.C - (byte*)p);
            layout[1] = (int)((byte*)(p + 1) - (byte*)p);
        }
    }

    [GpuKernel]
    public static void RoundtripCoreFieldKernel(ref AbiCoreFieldStruct data)
    {
        data.A = (byte)(data.A + 1);
        data.C = data.C + 1;
    }

    [GpuKernel]
    public static void MeasurePrivateInlineArrayKernel(int[] layout)
    {
        unsafe
        {
            AbiPrivateInlineArrayStruct s = default;
            AbiPrivateInlineArrayStruct* p = &s;
            layout[0] = (int)((byte*)&s.C - (byte*)p);
            layout[1] = (int)((byte*)(p + 1) - (byte*)p);
        }
    }

    [GpuKernel]
    public static void RoundtripPrivateInlineArrayKernel(ref AbiPrivateInlineArrayStruct data)
    {
        data.A = (byte)(data.A + 1);
        data.C = data.C + 1;
    }

    [GpuKernel]
    public static void MeasureFixedBufferKernel(int[] layout)
    {
        unsafe
        {
            AbiFixedBufferStruct s = default;
            AbiFixedBufferStruct* p = &s;
            layout[0] = (int)((byte*)&s.C - (byte*)p);
            layout[1] = (int)((byte*)(p + 1) - (byte*)p);
        }
    }

    [GpuKernel]
    public static void RoundtripFixedBufferKernel(ref AbiFixedBufferStruct data)
    {
        data.A = (byte)(data.A + 1);
        data.C = data.C + 1;
    }

    [GpuKernel]
    public static void MeasureAutoPropertyKernel(int[] layout)
    {
        unsafe
        {
            AbiAutoPropertyStruct s = default;
            AbiAutoPropertyStruct* p = &s;
            layout[0] = (int)((byte*)&s.C - (byte*)p);
            layout[1] = (int)((byte*)(p + 1) - (byte*)p);
        }
    }

    [GpuKernel]
    public static void RoundtripAutoPropertyKernel(ref AbiAutoPropertyStruct data)
    {
        data.A = (byte)(data.A + 1);
        data.B = data.B + 1.0;
        data.C = data.C + 1;
    }

    [GpuKernel]
    public static void MeasureComputedPropertyKernel(int[] layout)
    {
        unsafe
        {
            AbiComputedPropertyStruct s = default;
            AbiComputedPropertyStruct* p = &s;
            layout[0] = (int)((byte*)&s.C - (byte*)p);
            layout[1] = (int)((byte*)(p + 1) - (byte*)p);
        }
    }

    [GpuKernel]
    public static void RoundtripComputedPropertyKernel(ref AbiComputedPropertyStruct data)
    {
        data.C = data.C + 1;
    }

    [GpuKernel]
    public static void ReadComputedPropertyKernel(int[] result)
    {
        AbiComputedPropertyStruct s = default;
        s.A = 4;
        s.C = 7;
        result[0] = s.Sum;
        result[1] = s.Twice;
        result[2] = s.Product;
        result[3] = s.Difference;
    }

    [GpuKernel]
    public static void ReadComputedPropertyThroughRefKernel(ref AbiComputedPropertyStruct data, int[] result)
    {
        result[0] = data.Sum;
        result[1] = data.Twice;
        result[2] = data.Product;
        result[3] = data.Difference;
    }
}
