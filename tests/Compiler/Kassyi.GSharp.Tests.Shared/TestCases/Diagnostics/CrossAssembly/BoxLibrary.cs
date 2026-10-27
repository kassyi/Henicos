using System.Collections.Generic;
using Kassyi.GSharp.Core;

// A generic struct with a constructor, which the library uses as Box<int> only; an iterator; a function that depends on
// parallel execution
namespace XasmLib
{
    public struct Pair
    {
        public int A;

        public Pair(int a)
        {
            A = a;
        }
    }

    public struct Box<T>
    {
        public T Value;

        public Box(T value)
        {
            Value = value;
        }
    }

    public static partial class LibMath
    {
        [GpuDeviceFunction]
        public static int Unbox(int x) => new Box<int>(x).Value;

        // Pair (with a constructor) is recorded on its own
        [GpuDeviceFunction]
        public static int First(Pair pair) => pair.A;

        [GpuDeviceFunction]
        public static IEnumerable<int> Upto(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return i;
            }
        }

        [GpuDeviceFunction]
        public static void GridTouch(int n)
        {
            Gpu.GridStrideLoop(n, i => { });
        }
    }
}
