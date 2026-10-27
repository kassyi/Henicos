using Kassyi.GSharp.Core;
using XasmLib;

namespace CrossAssemblyConsumer
{
    public static partial class ForwardKernels
    {
        [GpuDeviceFunction]
        public static int Forward(int x)
        {
            return LibMath.Twice(x);
        }
    }
}
