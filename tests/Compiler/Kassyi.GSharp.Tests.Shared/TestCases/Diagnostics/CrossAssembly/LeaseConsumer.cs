using Kassyi.GSharp.Core;
using XasmLib;

namespace CrossAssemblyConsumer
{
    public static partial class LeaseKernels
    {
        [GpuKernel]
        public static void CallKernel(int[] data)
        {
            using var lease = new Lease();
            data[0] = lease.V;
        }
    }
}
