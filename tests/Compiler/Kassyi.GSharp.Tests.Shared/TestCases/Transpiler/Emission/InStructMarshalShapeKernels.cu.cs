#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

[SkipSnapshotConsistency("In-struct array unmarshaling shapes checked by EmissionShapeTests")]
public static partial class InStructMarshalShapeKernels
{
    public struct Bag
    {
        public int[] Data;
        public int Tag;
    }

    [GpuKernel]
    public static void SeparateInAndRefBagKernel(in Bag readOnlyBag, ref Bag mutableBag, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            readOnlyBag.Data[0] += 1;
            mutableBag.Data[0] += 2;
            mutableBag.Tag += 3;
            observed[0] = readOnlyBag.Tag;
        }
    }

    [GpuKernel]
    public static void TwoAliasedInBagsKernel(in Bag first, in Bag second, [Out] Bag[] sink, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            first.Data[0] += 1;
            second.Data[1] += 2;
            observed[0] = first.Tag + second.Tag + sink.Length;
        }
    }
}
