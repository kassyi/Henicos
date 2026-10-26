using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

public struct PlainElem
{
    public int X;
    public int Y;
}

/// <summary>An array field of user structs: its element needs a shadow struct of its own.</summary>
public struct ElemArrayHolder
{
    public int Tag;
    public PlainElem[] Items;
}

// Without the element's shadow (TypeLayoutCalculator.CollectStructs) the generated wrapper does not compile
[SkipSnapshotConsistency]
public static partial class StructArrayFieldElementKernels
{
    [GpuKernel]
    public static void ElemArrayKernel(ref ElemArrayHolder h, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = h.Tag;
            o[1] = h.Items[1].Y;
            h.Items[0].X = 42;
        }
    }
}
