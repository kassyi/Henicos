using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

// Private enums of every underlying width behind an array field (the struct is converted member by member through the
// host mirror)
public partial struct EnumFieldPrivateJob
{
    private enum Mode { A = 0, C = 300 }

    private enum LongMode : long { Low = 0, High = 1L << 40 }

    private enum SByteMode : sbyte { Negative = -1, Positive = 1 }

    private enum ULongMode : ulong { Zero = 0, Top = 0x8000000000000001UL }

    public int[] Data;
    private Mode _mode;
    private LongMode _longMode;
    private SByteMode _sbyteMode;
    private ULongMode _ulongMode;

    public static EnumFieldPrivateJob Create(int[] data) => new()
    {
        Data = data,
        _mode = Mode.C,
        _longMode = LongMode.High,
        _sbyteMode = SByteMode.Negative,
        _ulongMode = ULongMode.Top,
    };

    public readonly bool IsFlipped => _mode == Mode.A && _longMode == LongMode.Low &&
                                      _sbyteMode == SByteMode.Positive && _ulongMode == ULongMode.Zero;

    [GpuDeviceFunction]
    public static void Read(EnumFieldPrivateJob job)
    {
        job.Data[0] = (int)job._mode;
        job.Data[1] = job._longMode == LongMode.High ? 1 : 0;
        job.Data[2] = (int)job._sbyteMode;
        job.Data[3] = job._ulongMode == ULongMode.Top ? 1 : 0;
    }

    [GpuDeviceFunction]
    public static void Flip(ref EnumFieldPrivateJob job)
    {
        job._mode = Mode.A;
        job._longMode = LongMode.Low;
        job._sbyteMode = SByteMode.Positive;
        job._ulongMode = ULongMode.Zero;
    }
}

// No array field: the struct is copied as one block, and only its shadow declares the private enum
public partial struct EnumFieldPrivateBlock
{
    private enum Mode : byte { X = 0, Y = 200 }

    public int Count;
    private Mode _mode;

    public static EnumFieldPrivateBlock Create(int count, bool raised) =>
        new() { Count = count, _mode = raised ? Mode.Y : Mode.X };

    [GpuDeviceFunction]
    public static int ModeOf(EnumFieldPrivateBlock block) => (int)block._mode;
}

// A private enum in a struct that a field of a nested struct holds
public partial struct EnumFieldPrivateOuter
{
    public EnumFieldPrivateJob Inner;
    public int Y;
}

// In a class of their own: a private enum type makes the generated context class and its pointer launchers private
public static partial class EnumFieldPrivateKernels
{
    [GpuKernel]
    public static void ReadPrivateEnumFieldsKernel(EnumFieldPrivateJob job)
    {
        if (ThreadIndex.X == 0)
        {
            EnumFieldPrivateJob.Read(job);
        }
    }

    [GpuKernel]
    public static void WritePrivateEnumFieldsKernel(ref EnumFieldPrivateJob job)
    {
        if (ThreadIndex.X == 0)
        {
            EnumFieldPrivateJob.Flip(ref job);
        }
    }

    [GpuKernel]
    public static void ReadPrivateEnumBlockKernel(EnumFieldPrivateBlock block, int[] result)
    {
        result[ThreadIndex.X] = EnumFieldPrivateBlock.ModeOf(block) + block.Count;
    }

    [GpuKernel]
    public static void ReadPrivateEnumArrayKernel(EnumFieldPrivateBlock[] blocks, int[] result)
    {
        result[ThreadIndex.X] = EnumFieldPrivateBlock.ModeOf(blocks[ThreadIndex.X]);
    }

    [GpuKernel]
    public static void ReadPrivateEnumNestedKernel(EnumFieldPrivateOuter outer)
    {
        if (ThreadIndex.X == 0)
        {
            EnumFieldPrivateJob.Read(outer.Inner);
            outer.Inner.Data[4] = outer.Y;
        }
    }
}
