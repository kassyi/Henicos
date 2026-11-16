#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct WideIndexHolder
{
    public int[] Data;
}

public static partial class WideArrayIndexKernels
{
    [GpuDeviceFunction]
    private static int Side(int[] side)
    {
        side[0] += 1;
        return 77;
    }

    [GpuDeviceFunction]
    private static void AddToRef(ref int target, int delta)
    {
        target += delta;
    }

    [GpuDeviceFunction]
    private static int MutateAndReturn(int[] a, int delta)
    {
        a[0] += delta;
        return a[0] + 100;
    }

    [GpuDeviceFunction]
    private static int UncheckedSByte(int[] a, sbyte i0, sbyte i1)
    {
        a[i1] += a[i0];
        return a[i1];
    }

    [GpuDeviceFunction]
    private static int UncheckedByte(int[] a, byte i0, byte i1)
    {
        a[i1] += a[i0];
        return a[i1];
    }

    [GpuDeviceFunction]
    private static int UncheckedShort(int[] a, short i0, short i1)
    {
        a[i1] += a[i0];
        return a[i1];
    }

    [GpuDeviceFunction]
    private static int UncheckedUShort(int[] a, ushort i0, ushort i1)
    {
        a[i1] += a[i0];
        return a[i1];
    }

    [GpuDeviceFunction]
    private static int UncheckedChar(int[] a, char i0, char i1)
    {
        a[i1] += a[i0];
        return a[i1];
    }

    [GpuDeviceFunction]
    private static int UncheckedInt(int[] a, int i0, int i1)
    {
        a[i1] += a[i0];
        return a[i1];
    }

    [GpuDeviceFunction]
    private static int UncheckedUInt(int[] a, uint i0, uint i1)
    {
        a[i1] += a[i0];
        return a[i1];
    }

    [GpuDeviceFunction]
    private static int UncheckedLong(int[] a, long i0, long i1)
    {
        a[i1] += a[i0];
        return a[i1];
    }

    [GpuDeviceFunction]
    private static int UncheckedULong(int[] a, ulong i0, ulong i1)
    {
        a[i1] += a[i0];
        return a[i1];
    }

    [GpuKernel]
    public static void ExerciseSByteIndexKernel(int[] a, WideIndexHolder h, sbyte i0, sbyte i1, sbyte i2, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int read0 = a[i0];
        a[i1] = 20;
        a[i1] += 5;
        a[i1]++;
        AddToRef(ref a[i2], 40);
        h.Data[i1] = h.Data[i0] + 7;
        int uncheckedVal = UncheckedSByte(a, i0, i1);
        sbyte step = i2;
        a[step++] = step * 100 + MutateAndReturn(a, 3);
        sbyte rStep = i0;
        int seqRead = (rStep >= 0 ? a : a)[rStep++];
        o[0] = read0;
        o[1] = uncheckedVal;
        o[2] = seqRead + rStep;
    }

    [GpuKernel]
    public static void ExerciseByteIndexKernel(int[] a, WideIndexHolder h, byte i0, byte i1, byte i2, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int read0 = a[i0];
        a[i1] = 20;
        a[i1] += 5;
        a[i1]++;
        AddToRef(ref a[i2], 40);
        h.Data[i1] = h.Data[i0] + 7;
        int uncheckedVal = UncheckedByte(a, i0, i1);
        byte step = i2;
        a[step++] = step * 100 + MutateAndReturn(a, 3);
        byte rStep = i0;
        int seqRead = (rStep >= 0 ? a : a)[rStep++];
        o[0] = read0;
        o[1] = uncheckedVal;
        o[2] = seqRead + rStep;
    }

    [GpuKernel]
    public static void ExerciseShortIndexKernel(int[] a, WideIndexHolder h, short i0, short i1, short i2, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int read0 = a[i0];
        a[i1] = 20;
        a[i1] += 5;
        a[i1]++;
        AddToRef(ref a[i2], 40);
        h.Data[i1] = h.Data[i0] + 7;
        int uncheckedVal = UncheckedShort(a, i0, i1);
        short step = i2;
        a[step++] = step * 100 + MutateAndReturn(a, 3);
        short rStep = i0;
        int seqRead = (rStep >= 0 ? a : a)[rStep++];
        o[0] = read0;
        o[1] = uncheckedVal;
        o[2] = seqRead + rStep;
    }

    [GpuKernel]
    public static void ExerciseUShortIndexKernel(int[] a, WideIndexHolder h, ushort i0, ushort i1, ushort i2, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int read0 = a[i0];
        a[i1] = 20;
        a[i1] += 5;
        a[i1]++;
        AddToRef(ref a[i2], 40);
        h.Data[i1] = h.Data[i0] + 7;
        int uncheckedVal = UncheckedUShort(a, i0, i1);
        ushort step = i2;
        a[step++] = step * 100 + MutateAndReturn(a, 3);
        ushort rStep = i0;
        int seqRead = (rStep >= 0 ? a : a)[rStep++];
        o[0] = read0;
        o[1] = uncheckedVal;
        o[2] = seqRead + rStep;
    }

    [GpuKernel]
    public static void ExerciseCharIndexKernel(int[] a, WideIndexHolder h, char i0, char i1, char i2, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int read0 = a[i0];
        a[i1] = 20;
        a[i1] += 5;
        a[i1]++;
        AddToRef(ref a[i2], 40);
        h.Data[i1] = h.Data[i0] + 7;
        int uncheckedVal = UncheckedChar(a, i0, i1);
        char step = i2;
        a[step++] = step * 100 + MutateAndReturn(a, 3);
        char rStep = i0;
        int seqRead = (rStep >= 0 ? a : a)[rStep++];
        o[0] = read0;
        o[1] = uncheckedVal;
        o[2] = seqRead + rStep;
    }

    [GpuKernel]
    public static void ExerciseIntIndexKernel(int[] a, WideIndexHolder h, int i0, int i1, int i2, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int read0 = a[i0];
        a[i1] = 20;
        a[i1] += 5;
        a[i1]++;
        AddToRef(ref a[i2], 40);
        h.Data[i1] = h.Data[i0] + 7;
        int uncheckedVal = UncheckedInt(a, i0, i1);
        int step = i2;
        a[step++] = step * 100 + MutateAndReturn(a, 3);
        int rStep = i0;
        int seqRead = (rStep >= 0 ? a : a)[rStep++];
        o[0] = read0;
        o[1] = uncheckedVal;
        o[2] = seqRead + rStep;
    }

    [GpuKernel]
    public static void ExerciseUIntIndexKernel(int[] a, WideIndexHolder h, uint i0, uint i1, uint i2, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int read0 = a[i0];
        a[i1] = 20;
        a[i1] += 5;
        a[i1]++;
        AddToRef(ref a[i2], 40);
        h.Data[i1] = h.Data[i0] + 7;
        int uncheckedVal = UncheckedUInt(a, i0, i1);
        uint step = i2;
        a[step++] = (int)step * 100 + MutateAndReturn(a, 3);
        uint rStep = i0;
        int seqRead = ((int)rStep >= 0 ? a : a)[rStep++];
        o[0] = read0;
        o[1] = uncheckedVal;
        o[2] = seqRead + (int)rStep;
    }

    [GpuKernel]
    public static void ExerciseLongIndexKernel(int[] a, WideIndexHolder h, long i0, long i1, long i2, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int read0 = a[i0];
        a[i1] = 20;
        a[i1] += 5;
        a[i1]++;
        AddToRef(ref a[i2], 40);
        h.Data[i1] = h.Data[i0] + 7;
        int uncheckedVal = UncheckedLong(a, i0, i1);
        long step = i2;
        a[step++] = (int)step * 100 + MutateAndReturn(a, 3);
        long rStep = i0;
        int seqRead = ((int)rStep >= 0 ? a : a)[rStep++];
        o[0] = read0;
        o[1] = uncheckedVal;
        o[2] = seqRead + (int)rStep;
    }

    [GpuKernel]
    public static void ExerciseULongIndexKernel(int[] a, WideIndexHolder h, ulong i0, ulong i1, ulong i2, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int read0 = a[i0];
        a[i1] = 20;
        a[i1] += 5;
        a[i1]++;
        AddToRef(ref a[i2], 40);
        h.Data[i1] = h.Data[i0] + 7;
        int uncheckedVal = UncheckedULong(a, i0, i1);
        ulong step = i2;
        a[step++] = (int)step * 100 + MutateAndReturn(a, 3);
        ulong rStep = i0;
        int seqRead = ((int)rStep >= 0 ? a : a)[rStep++];
        o[0] = read0;
        o[1] = uncheckedVal;
        o[2] = seqRead + (int)rStep;
    }

    [GpuKernel]
    public static void ReadLongIndexKernel(int[] a, long index, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = a[index];
        }
    }

    [GpuKernel]
    public static void WriteLongIndexKernel(int[] a, long index, int value)
    {
        if (ThreadIndex.X == 0)
        {
            a[index] = value;
        }
    }

    [GpuKernel]
    public static void ReadULongIndexKernel(int[] a, ulong index, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = a[index];
        }
    }

    [GpuKernel]
    public static void WriteULongIndexKernel(int[] a, ulong index, int value)
    {
        if (ThreadIndex.X == 0)
        {
            a[index] = value;
        }
    }

    // The index lives across a barrier: the state machine reads it from its frame
    [GpuKernel]
    public static void ReadULongIndexAcrossBarrierKernel(int[] a, ulong index, int[] o)
    {
        ulong kept = index;
        Gpu.SyncThreads();
        o[0] = a[kept];
    }

    // A warp shuffle on the right side: lowering spills the target's array and index before the shuffle
    [GpuKernel]
    public static void WriteULongIndexShuffleKernel(int[] a, ulong index, int value)
    {
        a[index] = Warp.ShuffleSync(1u, value, 0);
    }

    [GpuKernel]
    public static void ReadUIntIndexKernel(int[] a, uint index, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = a[index];
        }
    }

    [GpuKernel]
    public static void WriteUIntIndexKernel(int[] a, uint index, int value)
    {
        if (ThreadIndex.X == 0)
        {
            a[index] = value;
        }
    }

    [GpuDeviceFunction]
    private static int ReadUncheckedLongDevice(int[] a, long index) => a[index];

    [GpuDeviceFunction]
    private static int ReadUncheckedULongDevice(int[] a, ulong index) => a[index];

    [GpuDeviceFunction]
    private static int ReadUncheckedUIntDevice(int[] a, uint index) => a[index];

    [GpuKernel]
    public static void ReadUncheckedLongIndexKernel(int[] a, long index, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = ReadUncheckedLongDevice(a, index);
        }
    }

    [GpuKernel]
    public static void ReadUncheckedULongIndexKernel(int[] a, ulong index, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = ReadUncheckedULongDevice(a, index);
        }
    }

    [GpuKernel]
    public static void ReadUncheckedUIntIndexKernel(int[] a, uint index, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = ReadUncheckedUIntDevice(a, index);
        }
    }

    [GpuKernel]
    public static void ReadNullWithULongIndexKernel(int[]? a, ulong index, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
#pragma warning disable CS8602 // C#: tests the exception order between a null array and a ulong index
            o[0] = a[index];
#pragma warning restore CS8602
        }
    }

    [GpuKernel]
    public static void ReadNullWithLongIndexKernel(int[]? a, long index, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
#pragma warning disable CS8602 // C#: tests the exception order between a null array and a long index
            o[0] = a[index];
#pragma warning restore CS8602
        }
    }

    [GpuKernel]
    public static void ReadReceiverSideEffectWithULongIndexKernel(int[] a, ulong index, int[] side, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = (Side(side) > 0 ? a : a)[index];
        }
    }

    [GpuKernel]
    public static void WriteULongIndexWithSideEffectKernel(int[] a, ulong index, int[] side)
    {
        if (ThreadIndex.X == 0)
        {
            a[index] = Side(side);
        }
    }

    [GpuKernel]
    public static void WriteLongIndexWithSideEffectKernel(int[] a, long index, int[] side)
    {
        if (ThreadIndex.X == 0)
        {
            a[index] = Side(side);
        }
    }
}
