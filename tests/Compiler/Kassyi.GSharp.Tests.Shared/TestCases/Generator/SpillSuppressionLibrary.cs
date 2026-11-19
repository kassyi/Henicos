using Kassyi.GSharp.Core;

// A library whose device functions call each other: a consumer's call graph sees only the names it calls itself
namespace SpillSuppressionLibrary
{
    public static partial class Lib
    {
        // Recursive: reached from the marked kernel directly and from the unmarked kernel through G
        [GpuDeviceFunction]
        public static int F(int x) => x <= 0 ? 0 : F(x - 1) + 1;

        [GpuDeviceFunction]
        public static int G(int x) => F(x) * 2;

        // Reached only from the marked kernel, through Relay
        [GpuDeviceFunction]
        public static int Only(int x) => x + 3;

        [GpuDeviceFunction]
        public static int Relay(int x) => Only(x) * 5;
    }
}
