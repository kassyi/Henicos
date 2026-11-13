using Kassyi.GSharp.Core;

namespace FileLocalRepro
{
    file struct LocalValue
    {
        public int A;
    }

    file static partial class LocalMath
    {
        [GpuDeviceFunction]
        public static int Twice(int x) => x * 2;

        [GpuDeviceFunction]
        public static int Thrice(this int x) => x * 3;
    }

    public static partial class LocalKernels
    {
        // The managed path copies the body into a generated file, where the file-local type is not visible (#427)
        [GpuKernel]
        public static void Run(int[] data)
        {
            LocalValue v = new LocalValue { A = 1 };
            data[ThreadIndex.X] = v.A;
        }

        [GpuKernel]
        public static void CallStatic(int[] data)
        {
            data[ThreadIndex.X] = LocalMath.Twice(data[ThreadIndex.X]);
        }

        [GpuKernel]
        public static void CallExtension(int[] data)
        {
            data[ThreadIndex.X] = data[ThreadIndex.X].Thrice();
        }
    }

    public static partial class SuspendingKernels
    {
        // The state machine of a suspending device function is a copy of its body as well
        [GpuDeviceFunction]
        public static int Wait(int value)
        {
            LocalValue v = new LocalValue { A = value };
            Gpu.SyncThreads();
            return v.A;
        }

        [GpuKernel]
        public static void Run(int[] data)
        {
            data[ThreadIndex.X] = Wait(data[ThreadIndex.X]);
        }
    }

    public static partial class PlainKernels
    {
        // A device function that the managed path calls as it is: its body is not copied
        [GpuDeviceFunction]
        public static int Plain(int value)
        {
            LocalValue v = new LocalValue { A = value };
            return v.A;
        }

        [GpuKernel]
        public static void Run(int[] data)
        {
            data[ThreadIndex.X] = Plain(data[ThreadIndex.X]);
        }
    }
}
