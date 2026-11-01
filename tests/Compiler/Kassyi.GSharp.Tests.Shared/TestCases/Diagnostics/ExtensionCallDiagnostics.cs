using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics.ExtensionCalls
{
    public static partial class DeviceExtensions
    {
        [GpuDeviceFunction]
        public static int Plus(this int x, int y) => x + y;

        [GpuDeviceFunction]
        public static int AfterBarrier(this int x)
        {
            Gpu.SyncThreads();
            return x;
        }
    }

    // GSH0003 for an extension call of a device function that suspends, and for a StackLinq call with reordered interfering arguments
    public static partial class ExtensionCallKernels
    {
        [GpuKernel]
        public static void SuspendingExtensionKernel(int[] data)
        {
            data[ThreadIndex.X] = data[ThreadIndex.X].AfterBarrier();
        }

        [GpuKernel]
        public static void ReorderedStackLinqKernel(int[] data, int step)
        {
            data[ThreadIndex.X] = StackLinqExtensions.Take(
                    count: step++,
                    source: StackLinqExtensions.Skip(data.AsStackEnumerable(), step))
                .Sum();
        }

        [GpuKernel]
        public static void InterferingReceiverKernel(int[] data)
        {
            int value = data[ThreadIndex.X];
            data[ThreadIndex.X] = value.Plus(value++);
        }

        [GpuKernel]
        public static void IndependentReceiverKernel(int[] data, int step)
        {
            int value = data[ThreadIndex.X];
            data[ThreadIndex.X] = value.Plus(step) +
                StackLinqExtensions.Take(count: step, source: StackLinqExtensions.Skip(data.AsStackEnumerable(), 1)).Sum();
        }
    }
}
