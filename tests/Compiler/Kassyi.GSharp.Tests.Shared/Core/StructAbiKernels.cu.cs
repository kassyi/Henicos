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

// A computed property has no backing field, so it is no part of the host layout. Whether the device declares it as a
// member is pinned by StructAbiTests.ComputedPropertyStruct_DeviceDeclaresTheComputedProperty (#468).
public struct AbiComputedPropertyStruct
{
    public byte A;
    public int Sum => A + 1;
    public int C;
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
}
