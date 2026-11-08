using System.Numerics;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

public struct StackLinqMoney : IAdditionOperators<StackLinqMoney, StackLinqMoney, StackLinqMoney>,
    IAdditiveIdentity<StackLinqMoney, StackLinqMoney>
{
    public int Cents;

    public static StackLinqMoney AdditiveIdentity => default;

    public static StackLinqMoney operator +(StackLinqMoney left, StackLinqMoney right) =>
        new StackLinqMoney { Cents = left.Cents + right.Cents };
}

public static partial class TestKernelContainer
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void StackLinqStructSumKernel(StackLinqMoney[] items, int[] result)
    {
        // Sum adds with the user-defined + of the element type, which GPU code does not call: fused and unfused
        result[ThreadIndex.X] = items.AsStackEnumerable().AsParallel().Sum().Cents;
        var parallel = items.AsStackEnumerable().AsParallel();
        result[ThreadIndex.X] += parallel.Sum().Cents;
    }
}
