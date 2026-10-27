using Kassyi.GSharp.Core;

// A library without kernels and without unsafe code, built for CUDA, whose suspending device functions use types of
// every visibility and live in nested classes: its managed state machines must compile, or be left out
namespace StateMachineCase
{
    internal struct Hidden
    {
        public int V;
    }

    public static partial class LibSm
    {
        // Public with public types: a public state machine
        [GpuDeviceFunction]
        public static int WaitPublic(int[] buffer, int tid)
        {
            buffer[tid] = tid;
            Gpu.SyncThreads();
            return buffer[(tid + 1) % 4];
        }

        // An internal parameter type: an internal state machine
        [GpuDeviceFunction]
        internal static int WaitHidden(Hidden hidden, int[] buffer, int tid)
        {
            buffer[tid] = hidden.V;
            Gpu.SyncThreads();
            return buffer[(tid + 1) % 4];
        }

        // A local of a private nested struct: no state machine
        [GpuDeviceFunction]
        public static int WaitPrivate(int[] buffer, int tid)
        {
            var secret = new Secret();
            secret.V = tid;
            buffer[tid] = secret.V;
            Gpu.SyncThreads();
            return buffer[(tid + 1) % 4] + secret.V;
        }

        // Calls WaitPrivate: no state machine either
        [GpuDeviceFunction]
        public static int WaitThrough(int[] buffer, int tid)
        {
            int value = WaitPrivate(buffer, tid);
            Gpu.SyncThreads();
            return value + 1;
        }

        private struct Secret
        {
            public int V;
        }

        // A nested class: the state machine goes into LibSm.Inner
        public static partial class Inner
        {
            [GpuDeviceFunction]
            public static int WaitNested(int[] buffer, int tid)
            {
                buffer[tid] = tid * 2;
                Gpu.SyncThreads();
                return buffer[(tid + 1) % 4];
            }
        }
    }

    // A nested class in a class that is not partial: no state machine
    public static class Closed
    {
        public static partial class Open
        {
            [GpuDeviceFunction]
            public static int WaitClosed(int[] buffer, int tid)
            {
                buffer[tid] = tid;
                Gpu.SyncThreads();
                return buffer[0];
            }
        }
    }
}
