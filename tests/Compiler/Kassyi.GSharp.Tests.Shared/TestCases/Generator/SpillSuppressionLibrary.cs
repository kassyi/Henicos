using Kassyi.GSharp.Core;

// A library whose device functions and struct members call its device functions: a consumer's call graph sees only the
// names it calls itself, and the struct members of the library have no record of their own. (A `using` of a library
// ref struct is GSH0003: its Dispose has no body in the consumer, so it has no case here.)
namespace SpillSuppressionLibrary
{
    public struct LibHolder
    {
        public int V;

        public int Doubled => Lib.FGetter(V);
    }

    public struct LibHolder2
    {
        public int V;

        public int Doubled => Lib.FViaFn(V);
    }

    public struct LibBuilt
    {
        public int V;

        public LibBuilt(int v)
        {
            V = Lib.FCtor(v);
        }
    }

    public static partial class Lib
    {
        // Recursive: reached from the marked kernel directly and from the unmarked kernel through G
        [GpuDeviceFunction]
        public static int F(int x) => x <= 0 ? 0 : F(x - 1) + 1;

        [GpuDeviceFunction]
        public static int G(int x) => F(x) * 2;

        // A chain of two steps: G2 -> H -> F2
        [GpuDeviceFunction]
        public static int F2(int x) => x + 7;

        [GpuDeviceFunction]
        public static int H(int x) => F2(x) * 3;

        [GpuDeviceFunction]
        public static int G2(int x) => H(x) + 1;

        // Reached only from the marked kernel, through Relay
        [GpuDeviceFunction]
        public static int Only(int x) => x + 3;

        [GpuDeviceFunction]
        public static int Relay(int x) => Only(x) * 5;

        // Each reached by one unmarked kernel through a member of a library struct
        [GpuDeviceFunction]
        public static int FGetter(int x) => x + 11;

        [GpuDeviceFunction]
        public static int FViaFn(int x) => x + 12;

        [GpuDeviceFunction]
        public static int FCtor(int x) => x + 13;

        // A library function that uses a library struct's getter
        [GpuDeviceFunction]
        public static int UsesGetter(int x) => new LibHolder2 { V = x }.Doubled;
    }
}
