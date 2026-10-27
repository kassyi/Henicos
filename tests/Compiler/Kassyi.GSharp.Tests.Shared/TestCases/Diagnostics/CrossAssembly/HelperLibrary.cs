using Kassyi.GSharp.Core;

// Another library that declares the path of ChainLibrary.cs's internal Helper
namespace XasmLib
{
    public static partial class LibMath
    {
        [GpuDeviceFunction]
        public static int Helper(int x) => x + 100;
    }
}
