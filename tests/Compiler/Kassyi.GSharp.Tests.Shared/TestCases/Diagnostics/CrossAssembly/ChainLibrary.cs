using Kassyi.GSharp.Core;

// A public function that calls an internal one of the same library
namespace XasmLib
{
    public static partial class LibMath
    {
        [GpuDeviceFunction]
        public static int Quad(int x) => Helper(Helper(x));

        [GpuDeviceFunction]
        internal static int Helper(int x) => x * 2;
    }
}
