#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct WideIndexHolder
{
    public int[] Data;
}

public struct WideIndexBuffers
{
    public int[] Target;
    public WideIndexHolder Holder;
    public int[] Results;
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
    public static void ExerciseSByteIndexKernel(
        WideIndexBuffers buffers,
        sbyte firstIndex,
        sbyte secondIndex,
        sbyte thirdIndex)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int[] a = buffers.Target;
        WideIndexHolder h = buffers.Holder;
        int[] o = buffers.Results;
        sbyte i0 = firstIndex;
        sbyte i1 = secondIndex;
        sbyte i2 = thirdIndex;

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
    public static void ExerciseByteIndexKernel(
        WideIndexBuffers buffers,
        byte firstIndex,
        byte secondIndex,
        byte thirdIndex)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int[] a = buffers.Target;
        WideIndexHolder h = buffers.Holder;
        int[] o = buffers.Results;
        byte i0 = firstIndex;
        byte i1 = secondIndex;
        byte i2 = thirdIndex;

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
    public static void ExerciseShortIndexKernel(
        WideIndexBuffers buffers,
        short firstIndex,
        short secondIndex,
        short thirdIndex)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int[] a = buffers.Target;
        WideIndexHolder h = buffers.Holder;
        int[] o = buffers.Results;
        short i0 = firstIndex;
        short i1 = secondIndex;
        short i2 = thirdIndex;

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
    public static void ExerciseUShortIndexKernel(
        WideIndexBuffers buffers,
        ushort firstIndex,
        ushort secondIndex,
        ushort thirdIndex)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int[] a = buffers.Target;
        WideIndexHolder h = buffers.Holder;
        int[] o = buffers.Results;
        ushort i0 = firstIndex;
        ushort i1 = secondIndex;
        ushort i2 = thirdIndex;

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
    public static void ExerciseCharIndexKernel(
        WideIndexBuffers buffers,
        char firstIndex,
        char secondIndex,
        char thirdIndex)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int[] a = buffers.Target;
        WideIndexHolder h = buffers.Holder;
        int[] o = buffers.Results;
        char i0 = firstIndex;
        char i1 = secondIndex;
        char i2 = thirdIndex;

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
    public static void ExerciseIntIndexKernel(
        WideIndexBuffers buffers,
        int firstIndex,
        int secondIndex,
        int thirdIndex)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int[] a = buffers.Target;
        WideIndexHolder h = buffers.Holder;
        int[] o = buffers.Results;
        int i0 = firstIndex;
        int i1 = secondIndex;
        int i2 = thirdIndex;

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
    public static void ExerciseUIntIndexKernel(
        WideIndexBuffers buffers,
        uint firstIndex,
        uint secondIndex,
        uint thirdIndex)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int[] a = buffers.Target;
        WideIndexHolder h = buffers.Holder;
        int[] o = buffers.Results;
        uint i0 = firstIndex;
        uint i1 = secondIndex;
        uint i2 = thirdIndex;

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
    public static void ExerciseLongIndexKernel(
        WideIndexBuffers buffers,
        long firstIndex,
        long secondIndex,
        long thirdIndex)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int[] a = buffers.Target;
        WideIndexHolder h = buffers.Holder;
        int[] o = buffers.Results;
        long i0 = firstIndex;
        long i1 = secondIndex;
        long i2 = thirdIndex;

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
    public static void ExerciseULongIndexKernel(
        WideIndexBuffers buffers,
        ulong firstIndex,
        ulong secondIndex,
        ulong thirdIndex)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int[] a = buffers.Target;
        WideIndexHolder h = buffers.Holder;
        int[] o = buffers.Results;
        ulong i0 = firstIndex;
        ulong i1 = secondIndex;
        ulong i2 = thirdIndex;

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

    [GpuKernel]
    public static void ReadNUIntIndexKernel(int[] a, nuint index, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = a[index];
        }
    }

    [GpuKernel]
    public static void WriteNUIntIndexKernel(int[] a, nuint index, int value)
    {
        if (ThreadIndex.X == 0)
        {
            a[index] = value;
        }
    }

    // A warp shuffle on the right side: lowering spills the target's array and nuint index before the shuffle
    [GpuKernel]
    public static void WriteNUIntIndexShuffleKernel(int[] a, nuint index, int value)
    {
        a[index] = Warp.ShuffleSync(1u, value, 0);
    }

    [GpuDeviceFunction]
    private static int ReadUncheckedNUIntDevice(int[] a, nuint index) => a[index];

    [GpuKernel]
    public static void ReadUncheckedNUIntIndexKernel(int[] a, nuint index, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = ReadUncheckedNUIntDevice(a, index);
        }
    }

    [GpuKernel]
    public static void ReadConvertedULongIndexKernel(int[] a, WideULongIndex index, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = a[index];
        }
    }

    [GpuKernel]
    public static void WriteConvertedULongIndexKernel(int[] a, WideULongIndex index, int value)
    {
        if (ThreadIndex.X == 0)
        {
            a[index] = value;
        }
    }
}

/// <summary>An index wrapper that C# converts to <see cref="ulong"/> through a user-defined implicit conversion.</summary>
public struct WideULongIndex
{
    public ulong Value;

    public static implicit operator ulong(WideULongIndex index) => index.Value;
}
