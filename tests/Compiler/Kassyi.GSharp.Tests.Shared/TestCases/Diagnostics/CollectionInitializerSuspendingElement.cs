using System.Collections;
using Kassyi.GSharp.Core;

namespace CollectionInitializerSuspendingElementTest
{
    public struct ValueBag : IEnumerable
    {
        public int Sum;

        public IEnumerator GetEnumerator() => throw new System.NotSupportedException();
    }

    public struct InstanceBag : IEnumerable
    {
        public int Sum;

        public void Add(int value) { Sum += value; }

        public IEnumerator GetEnumerator() => throw new System.NotSupportedException();
    }

    public static partial class ValueBagExtensions
    {
        [GpuDeviceFunction]
        public static void Add(this ref ValueBag bag, int value) { bag.Sum += value; }
    }

    public static partial class SuspendingElementKernels
    {
        [GpuDeviceFunction]
        public static int Exchange(int value) => Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);

        [GpuKernel]
        public static void SuspendingElementKernel(int[] output)
        {
            int x = output[ThreadIndex.X];
            var called = new ValueBag { Exchange(x), 7 };
            var shuffled = new ValueBag { Warp.ShuffleXorSync(0xFFFFFFFFu, x, 1) };
            var conditional = new ValueBag { output.Length > 0 ? Exchange(x) : 0 };
            var instance = new InstanceBag { Exchange(x) };
            output[ThreadIndex.X] = called.Sum + shuffled.Sum + conditional.Sum + instance.Sum;
        }
    }
}
