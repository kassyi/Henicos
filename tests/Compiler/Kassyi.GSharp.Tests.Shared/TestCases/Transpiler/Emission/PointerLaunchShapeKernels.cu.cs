#nullable enable
using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

[SkipSnapshotConsistency("Pointer launcher shapes checked line by line by EmissionShapeTests")]
public static partial class PointerLaunchShapeKernels
{
    public struct Pair
    {
        public int First;
        public int Second;
    }

    // Each kind of parameter of the pointer launcher: a non-optional array, an optional array, a span, a ref scalar, a
    // by-value struct and a scalar
    [GpuKernel]
    public static void EveryKindKernel(int[] values, float[]? extra, Span<uint> words, ref int total, Pair pair, int count)
    {
        int i = ThreadIndex.X;
        if (i < count && i < values.Length && i < words.Length)
        {
            values[i] += pair.First + (int)words[i];
        }

        if (i == 0)
        {
            total = values.Length + (extra is null ? 0 : extra.Length) + pair.Second;
        }
    }

    // An automatic layout is the runtime's choice, not the C++ one: the pointer launcher takes its shadow struct
    [StructLayout(LayoutKind.Auto)]
    public struct Shuffled
    {
        public byte On;
        public long Value;
    }

    // The kinds that the CPU pointer launcher passes to the native launcher in their own form: a nullable array-like
    // value, a ref and a by-value struct whose layouts differ, an out scalar, bool and char scalars, and an auto result
    [GpuKernel]
    public static int NativeFormsKernel(Memory<int>? memory, ref Shuffled shuffled, Shuffled byValue, out int count,
        bool flag, char letter)
    {
        count = 0;
        if (memory.HasValue && ThreadIndex.X < memory.Value.Length)
        {
            memory.Value.Span[ThreadIndex.X] = letter;
        }

        return flag ? (int)shuffled.Value + (int)byValue.Value : byValue.On;
    }

    // Overloads: the public pointer launcher takes GpuBuffer<T> and forwards each array with its length
    [GpuKernel]
    public static void FillKernel(int[] output, ref int marker, int[]? optional = null)
    {
        output[ThreadIndex.X] = optional is null ? 0 : optional.Length;
        marker = 1;
    }

    [GpuKernel]
    public static void FillKernel(float[] output, ref int marker, float[]? optional = null)
    {
        output[ThreadIndex.X] = optional is null ? 0 : optional.Length;
        marker = 1;
    }
}
