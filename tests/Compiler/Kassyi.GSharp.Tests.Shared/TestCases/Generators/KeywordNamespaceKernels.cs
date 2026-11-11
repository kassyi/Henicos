using Kassyi.GSharp.Core;

// A namespace whose name is a keyword: the generated part of the kernel class reopens it as @event.P3, and the managed
// body names the struct it creates as global::@event.P3.Slot<int>
namespace @event.P3
{
    public struct Slot<T> where T : unmanaged
    {
        public int Count;

        public Slot(int count)
        {
            var shared = new SharedMemory<int>(4);
            shared[0] = count;
            Count = shared[0];
        }
    }

    public static partial class FillKernels
    {
        [GpuKernel]
        public static void FillKernel(int[] output)
        {
            var slot = new Slot<int>(output[0]);
            output[0] = slot.Count;
        }
    }
}

namespace KeywordDeclarations
{
    // A kernel class whose name is a keyword
    public static partial class @class
    {
        [GpuKernel]
        public static void ClassKernel(int[] output)
        {
            output[0] = 1;
        }
    }

    // A struct whose name is a keyword and that crosses to the device through a native shadow struct (it holds an
    // array), which the generated AotContexts class declares under the same name
    public struct @struct
    {
        public int[] Values;
        public int Count;
    }

    public static partial class StructKernels
    {
        [GpuKernel]
        public static void StructKernel(@struct data)
        {
            data.Values[0] = data.Count;
        }
    }

    // A kernel class nested in a type whose name is a keyword: the generated part reopens partial class @operator
    public static partial class @operator
    {
        public static partial class Nested
        {
            [GpuKernel]
            public static void NestedKernel(int[] output)
            {
                output[0] = 2;
            }
        }
    }
}
