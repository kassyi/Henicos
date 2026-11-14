using System.Collections;
using Kassyi.GSharp.Core;

namespace CollectionInitializerUnsupportedTest
{
    public struct InstanceBag : IEnumerable
    {
        public int Sum;

        public void Add(int x) { Sum += x; }

        public void Add(int x, int y) { Sum += x * y; }

        public IEnumerator GetEnumerator() => throw new System.NotSupportedException();
    }

    public struct UnmarkedBag : IEnumerable
    {
        public int Sum;

        public IEnumerator GetEnumerator() => throw new System.NotSupportedException();
    }

    public static class UnmarkedBagExtensions
    {
        public static void Add(this ref UnmarkedBag bag, int x) { bag.Sum += x; }
    }

    public struct Indexed
    {
        public int Sum;

        public int this[int index]
        {
            get => Sum;
            set => Sum += index * value;
        }
    }

    public struct Holder
    {
        public InstanceBag Bag;
        public Indexed Table;
        public int[] Values;

        public Holder(int[] values)
        {
            Bag = default;
            Table = default;
            Values = values;
        }
    }

    public static partial class CollectionInitializerKernels
    {
        [GpuKernel]
        public static void UnsupportedKernel(int[] output)
        {
            var single = new InstanceBag { 1, 2 };
            var multi = new InstanceBag { { 3, 4 } };
            var unmarked = new UnmarkedBag { 5 };
            var nested = new Holder { Bag = { 6 }, Table = { [1] = 7 } };
            var indexed = new Indexed { [2] = 8 };
            var array = new Holder(output) { Values = { [1] = 9 } };
            var barrier = new BarrierBag { 10 };
            output[0] = single.Sum + multi.Sum + unmarked.Sum + nested.Bag.Sum + nested.Table.Sum + indexed.Sum + barrier.Sum;
        }
    }

    public struct BarrierBag : IEnumerable
    {
        public int Sum;

        public IEnumerator GetEnumerator() => throw new System.NotSupportedException();
    }

    public static partial class BarrierBagExtensions
    {
        [GpuDeviceFunction]
        public static void Add(this ref BarrierBag bag, int x)
        {
            Gpu.SyncThreads();
            bag.Sum += x;
        }
    }
}
