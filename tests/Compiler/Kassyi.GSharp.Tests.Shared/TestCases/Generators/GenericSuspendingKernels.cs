using Kassyi.GSharp.Core;

// A suspending device function of a generic class: its frame types go into a generated part of G<T>, which a kernel
// of another class names as G<int>.<frame>
namespace GenericSuspending
{
    public static partial class G<T> where T : unmanaged
    {
        [GpuDeviceFunction]
        public static void Wait(int[] data)
        {
            Gpu.SyncThreads();
            data[ThreadIndex.X] += 1;
        }
    }

    public static partial class K
    {
        [GpuKernel]
        public static void UseKernel(int[] data) => G<int>.Wait(data);
    }
}
