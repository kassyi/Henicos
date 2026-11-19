using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

public enum EnumFieldMode
{
    A = 0,
    B = 1,
    C = 300,
}

public enum EnumFieldByteMode : byte
{
    X = 0,
    Y = 200,
}

// A value above 32 bits: a copy that keeps only 32 bits reads it as zero
public enum EnumFieldLongMode : long
{
    Low = 0,
    High = 1L << 40,
}

// The negative value and the top bit show a copy through a wrong native type (unsigned char, long long)
public enum EnumFieldSByteMode : sbyte
{
    Negative = -1,
    Positive = 1,
}

public enum EnumFieldULongMode : ulong
{
    Zero = 0,
    Top = 0x8000000000000001UL,
}

public struct EnumFieldInner
{
    public EnumFieldMode Mode;
}

// An array field makes the struct marshaled member by member; the enums sit next to it, one per underlying type, one
// behind an auto-property and one in a nested struct
public struct EnumFieldJob
{
    public int[] Data;
    public EnumFieldMode Mode;
    public EnumFieldByteMode ByteMode;
    public EnumFieldLongMode LongMode;
    public EnumFieldMode Property { get; set; }
    public EnumFieldInner Nested;
    public EnumFieldSByteMode SByteMode;
    public EnumFieldULongMode ULongMode;
}

public static partial class EnumFieldMarshalingKernels
{
    [GpuKernel]
    public static void ReadEnumFieldsKernel(EnumFieldJob job)
    {
        if (ThreadIndex.X == 0)
        {
            job.Data[0] = (int)job.Mode;
            job.Data[1] = (int)job.ByteMode;
            job.Data[2] = job.LongMode == EnumFieldLongMode.High ? 1 : 0;
            job.Data[3] = (int)job.Property;
            job.Data[4] = (int)job.Nested.Mode;
            job.Data[5] = (int)job.SByteMode;
            job.Data[6] = job.ULongMode == EnumFieldULongMode.Top ? 1 : 0;
        }
    }

    [GpuKernel]
    public static void WriteEnumFieldsKernel(ref EnumFieldJob job)
    {
        if (ThreadIndex.X == 0)
        {
            job.Mode = EnumFieldMode.C;
            job.ByteMode = EnumFieldByteMode.Y;
            job.LongMode = EnumFieldLongMode.High;
            job.Property = EnumFieldMode.B;
            job.Nested.Mode = EnumFieldMode.C;
            job.SByteMode = EnumFieldSByteMode.Negative;
            job.ULongMode = EnumFieldULongMode.Top;
        }
    }
}
