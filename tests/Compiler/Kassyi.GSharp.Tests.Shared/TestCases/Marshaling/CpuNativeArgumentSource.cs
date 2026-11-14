#nullable enable
using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Native;

// The same layout in C# and in the CPU C++: passed by the address of the host value
public struct Pair
{
    public int First;
    public int Second;
}

// An automatic layout is the runtime's choice, not the CPU C++ one: passed through its native shadow struct
[StructLayout(LayoutKind.Auto)]
public struct Shuffled
{
    public byte On;
    public long Value;
}

// An array field becomes a GSharpArrayProxy in the CPU C++: passed through its native shadow struct
public struct Holder
{
    public int[] Values;
}

// One kernel per kind of parameter the CPU launcher passes to the native launcher, and the auto results
public static partial class Arguments
{
    [GpuKernel]
    public static void ArrayKinds(int[] values, float[]? extra, Span<uint> words, Memory<int>? memory)
    {
    }

    [GpuKernel]
    public static void ReferenceKinds(ref int total, out int count, ref Pair pair, ref Shuffled shuffled)
    {
        count = total + pair.First + (int)shuffled.Value;
    }

    [GpuKernel]
    public static void StructKinds(Pair pair, Shuffled shuffled, Holder holder, int scale)
    {
    }

    // A Nullable<T> goes by address like a struct (#961): the host value when its layouts agree, its native shadow
    // when its value is converted (an array field)
    [GpuKernel]
    public static void NullableKinds(int? count, Holder? holder, ref int? total)
    {
        total = count;
    }

    [GpuKernel]
    public static int ScalarResult(int x) => x;

    [GpuKernel]
    public static Shuffled ConvertedResult(int x) => default;
}
