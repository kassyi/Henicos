using Kassyi.GSharp.Core;
using XasmLib;

namespace CrossAssemblyConsumer
{
    // new T() calls the parameterless constructor of the type argument, Counter() of the library
    public struct Holder<T>() where T : struct
    {
        public T Value = new T();
    }

    public static partial class ConsumerKernels
    {
        [GpuKernel]
        public static void CountKernel(int[] data)
        {
            var holder = new Holder<Counter>();
            data[0] = holder.Value.Value;
        }
    }
}
