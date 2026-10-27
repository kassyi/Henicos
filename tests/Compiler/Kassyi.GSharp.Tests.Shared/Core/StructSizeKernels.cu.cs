using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

// StructLayout.Size is a multiple of the alignment: 16 bytes on both sides, so the array is copied as one block
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct StructSizePaddedInt
{
    public int A;
}

// StructLayout.Size is not a multiple of the alignment: a 6-byte stride on the host, 8 bytes on the device (C++ sizeof
// is a multiple of alignof), so the launcher converts the array element by element
[StructLayout(LayoutKind.Sequential, Size = 6)]
public struct StructSizeOddInt
{
    public int A;
}

// Pack = 1 and Size = 7: the fields end at 5, so the device pads 2 bytes inside #pragma pack
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 7)]
public struct StructSizePacked
{
    public byte B;
    public int I;
}

public static partial class StructSizeKernels
{
    // Reads the second element (at the host stride) and writes the first one back; o[1] is the device size
    [GpuKernel]
    public static void ReadSecondPaddedKernel(StructSizePaddedInt[] data, int[] o)
    {
        o[0] = data[1].A;
        data[0].A = data[1].A + 1;
        unsafe
        {
            StructSizePaddedInt s = default;
            StructSizePaddedInt* p = &s;
            o[1] = (int)((byte*)(p + 1) - (byte*)p);
        }
    }

    [GpuKernel]
    public static void ReadSecondOddKernel(StructSizeOddInt[] data, int[] o)
    {
        o[0] = data[1].A;
        data[0].A = data[1].A + 1;
        unsafe
        {
            StructSizeOddInt s = default;
            StructSizeOddInt* p = &s;
            o[1] = (int)((byte*)(p + 1) - (byte*)p);
        }
    }

    // #pragma pack(1) with a 2-byte tail pad: 7 bytes on both sides
    [GpuKernel]
    public static void ReadSecondPackedKernel(StructSizePacked[] data, int[] o)
    {
        o[0] = data[1].I;
        data[0].I = data[1].I + 1;
        unsafe
        {
            StructSizePacked s = default;
            StructSizePacked* p = &s;
            o[1] = (int)((byte*)(p + 1) - (byte*)p);
        }
    }

    [GpuKernel]
    public static void ReadPaddedByValueKernel(StructSizePaddedInt value, int[] o)
    {
        o[0] = value.A;
    }
}
