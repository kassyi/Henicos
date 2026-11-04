using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

// B is declared first at offset 8, A second at offset 0: 4 bytes of gap before B and 16 bytes after it up to Size
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct GappedExplicit
{
    [FieldOffset(8)]
    public long B;

    [FieldOffset(0)]
    public int A;
}

// I and L share offset 0 (a union, the larger first); After follows the union alone
[StructLayout(LayoutKind.Explicit)]
public struct OverlapExplicit
{
    [FieldOffset(0)]
    public int I;

    [FieldOffset(0)]
    public long L;

    [FieldOffset(8)]
    public int After;
}

[StructLayout(LayoutKind.Sequential, Size = 5)]
public struct SizedFive
{
    public int A;
    public byte B;
}

[StructLayout(LayoutKind.Explicit)]
public struct ConvertedTailExplicit
{
    [FieldOffset(0)]
    public int I;

    [FieldOffset(0)]
    public long L;

    [FieldOffset(8)]
    public SizedFive Tail;
}

public struct ChainToDefault
{
    public int A;

    public ChainToDefault(int a) : this()
    {
        A = a;
    }
}

public struct ChainedGeneric<T>
    where T : unmanaged
{
    public T V;
    public int K;

    public ChainedGeneric(T v)
    {
        V = v;
        K = 0;
    }

    public ChainedGeneric(T v, int k) : this(v)
    {
        K = k;
    }
}

public struct ReadOnlyMembers
{
    [GpuReadOnly]
    public int Limit;

    [GpuReadOnly]
    public int[] Table;

    public int Plain;
}

[SkipSnapshotConsistency("Emission shapes checked line by line by EmissionShapeTests")]
public static partial class StructShapeKernels
{
    [GpuKernel]
    public static void ExplicitLayoutKernel(GappedExplicit gapped, OverlapExplicit overlap, ConvertedTailExplicit tail, long[] output)
    {
        output[0] = gapped.A + gapped.B + overlap.L + overlap.After + tail.L + tail.Tail.A;
    }

    [GpuKernel]
    public static void ChainedConstructorKernel(int[] output)
    {
        var plain = new ChainToDefault(3);
        var generic = new ChainedGeneric<int>(4, 5);
        output[0] = plain.A + generic.V + generic.K;
    }

    [GpuKernel]
    public static void ReadOnlyMemberKernel(ReadOnlyMembers members, ReadOnlyMembers[] pairs, int[] output)
    {
        output[0] = members.Limit + members.Table[0] + members.Plain + pairs[0].Plain;
    }
}
