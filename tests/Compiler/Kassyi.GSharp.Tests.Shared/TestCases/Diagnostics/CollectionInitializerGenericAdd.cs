using System.Collections;
using Kassyi.GSharp.Core;

namespace CollectionInitializerGenericAddTest
{
    public struct GenericBag : IEnumerable
    {
        public int Count;

        public IEnumerator GetEnumerator() => throw new System.NotSupportedException();
    }

    public static partial class GenericBagExtensions
    {
        [GpuDeviceFunction]
        public static void Add<T>(this ref GenericBag bag, T value) { bag.Count++; }
    }

    public static partial class GenericAddKernels
    {
        [GpuKernel]
        public static void GenericAddKernel(int[] output)
        {
            var generic = new GenericBag { output[0], 2.5 };
            output[0] = generic.Count;
        }
    }
}
