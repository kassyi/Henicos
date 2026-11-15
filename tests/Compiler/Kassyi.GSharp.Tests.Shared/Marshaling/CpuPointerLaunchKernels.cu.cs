#nullable enable
using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>A negative value of a 16-bit enum, so a widening of the parameter shows.</summary>
public enum PointerLaunchLevel : short
{
    Deep = -300,
    Shallow = 7,
}

/// <summary>A struct with its fields in declaration order.</summary>
public struct PointerLaunchPair
{
    public int First;
    public long Second;
    public byte Tag;
}

/// <summary>A struct whose host layout the runtime chooses, so the launcher passes its shadow.</summary>
[StructLayout(LayoutKind.Auto)]
public struct PointerLaunchShuffled
{
    public byte On;
    public long Value;
    public short Small;
}

/// <summary>A struct that holds an array, which crosses as a <c>GSharpArrayProxy</c> member of its shadow.</summary>
public struct PointerLaunchHolder
{
    public int[] Data;
    public int Scale;
}

[SkipSnapshotConsistency("Checked by the values that CpuPointerLaunchTests reads through Launch{Kernel}Ptr")]
public static partial class CpuPointerLaunchKernels
{
    // Every scalar kind, each at its own output index: a wrong width or a swapped argument moves a value
    [GpuKernel]
    public static void ScalarsKernel(bool flag, char letter, sbyte s8, byte u8, short s16, ushort u16, int s32,
        uint u32, long s64, ulong u64, PointerLaunchLevel level, float f32, double f64, long[] integers,
        double[] reals)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        integers[0] = flag ? 1 : 0;
        integers[1] = letter;
        integers[2] = s8;
        integers[3] = u8;
        integers[4] = s16;
        integers[5] = u16;
        integers[6] = s32;
        integers[7] = u32;
        integers[8] = s64;
        integers[9] = (long)u64;
        integers[10] = (long)level;
        reals[0] = f32;
        reals[1] = f64;
    }

    [GpuKernel]
    public static void RefAndOutKernel(ref int counter, out long big, ref bool flag, ref char letter, int[] data)
    {
        counter += data.Length;
        big = long.MinValue + 3;
        flag = !flag;
        letter = (char)(letter + 1);
    }

    [GpuKernel]
    public static void StructsKernel(PointerLaunchPair pair, PointerLaunchShuffled shuffled, int scale,
        long[] output)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        output[0] = pair.First * scale;
        output[1] = pair.Second;
        output[2] = pair.Tag;
        output[3] = shuffled.On;
        output[4] = shuffled.Value;
        output[5] = shuffled.Small;
    }

    [GpuKernel]
    public static void HolderKernel(PointerLaunchHolder holder, int[] output)
    {
        int i = ThreadIndex.X;
        if (i < holder.Data.Length && i < output.Length)
        {
            output[i] = holder.Data[i] * holder.Scale;
        }
    }

    // A null optional array is told from an empty one, and a nullable Memory<int> from a default one
    [GpuKernel]
    public static void OptionalKernel(int[]? optional, Memory<int>? memory, int[] output)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        output[0] = optional is null ? -1 : optional.Length;
        output[1] = memory.HasValue ? memory.Value.Length : -1;
        output[2] = optional is null || optional.Length == 0 ? 0 : optional[0];
    }

    // The same buffer may be all three arguments
    [GpuKernel]
    public static void AliasedKernel(int[] a, int[] b, int[] c)
    {
        int i = ThreadIndex.X;
        if (i < c.Length)
        {
            c[i] = a[i] + b[i];
        }
    }
}
