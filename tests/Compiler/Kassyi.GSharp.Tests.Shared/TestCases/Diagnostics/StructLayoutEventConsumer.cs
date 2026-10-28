using Kassyi.GSharp.Core;
using StructLayoutEventLibrary;

namespace StructLayoutEventConsumer
{
    public static partial class EventConsumerKernels
    {
        [GpuKernel]
        public static void LibraryEventKernel(LibraryWithEvent w, LibraryWithAccessorEvent c, int[] r) { r[0] = w.A + c.A; }
    }
}
