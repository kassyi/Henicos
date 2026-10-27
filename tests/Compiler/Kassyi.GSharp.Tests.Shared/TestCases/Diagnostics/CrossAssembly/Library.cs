using Kassyi.GSharp.Core;

// A library: a device function and a struct with a constructor
namespace XasmLib
{
    public static partial class LibMath
    {
        [GpuDeviceFunction]
        public static int Twice(int x) => x * 2;
    }

    public struct Pair
    {
        public int A;

        public Pair(int a)
        {
            A = a;
        }
    }
}
