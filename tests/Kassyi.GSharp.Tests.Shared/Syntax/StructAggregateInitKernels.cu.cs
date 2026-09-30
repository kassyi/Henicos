// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct InnerStruct
{
    public int X;
}

public struct OuterStruct
{
    public InnerStruct B;
    public int Y;
}

public struct AggregateStruct
{
    public int A;
    public int B;
}

public static partial class StructAggregateInitKernels
{
    [GpuKernel]
    public static void AggregateInitKernel(int[] output)
    {
        AggregateStruct s = new AggregateStruct { A = 1, B = 2 };
        output[0] = s.A + s.B;
    }

    [GpuKernel]
    public static void DeeplyNestedInitKernel(int[] output)
    {
        OuterStruct outer = new OuterStruct { B = new InnerStruct { X = 1 }, Y = 10 };
        output[0] = outer.B.X + outer.Y;
    }
}
