using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// Converted (it holds an array); its private field has a private nested type that the generated launcher cannot name
public struct HiddenTypeHolder
{
    public int[] Data;
    private Hidden _hidden;

    public readonly int Value => _hidden.V;

    private struct Hidden
    {
        public int V;
    }
}

// Not converted (no array): copied as a whole, but the layout guard of the nested struct names its type
public struct HiddenTypeIdentical
{
    public int X;
    private Hidden _hidden;

    public readonly int Value => _hidden.V;

    private struct Hidden
    {
        public int V;
    }
}

// The private nested struct holds an array, so it is converted on its own
public struct HiddenConvertedTypeHolder
{
    public int Tag;
    private Hidden _hidden;

    public readonly int Value => _hidden.Values[0];

    private struct Hidden
    {
        public int[] Values;
    }
}

// The field of private type sits in a nested struct: the path names both fields
public struct HiddenTypeOuter
{
    public int[] Data;
    public HiddenTypeHolder Inner;
}

// The private type is the element of an array field
public struct HiddenElementHolder
{
    public int[] Data;
    private Item[] _items;

    public readonly int First => _items[0].V;

    private struct Item
    {
        public int V;
    }
}

public static partial class PrivateFieldTypeInaccessibleKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void HiddenTypeKernel(HiddenTypeHolder holder)
    {
        holder.Data[0] = holder.Value;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void HiddenTypeIdenticalKernel(HiddenTypeIdentical holder, int[] output)
    {
        output[0] = holder.Value + holder.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void HiddenConvertedTypeKernel(HiddenConvertedTypeHolder holder, int[] output)
    {
        output[0] = holder.Value + holder.Tag;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void HiddenTypeOuterKernel(HiddenTypeOuter outer)
    {
        outer.Data[0] = outer.Inner.Value;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void HiddenElementKernel(HiddenElementHolder holder)
    {
        holder.Data[0] = holder.First;
    }
}
