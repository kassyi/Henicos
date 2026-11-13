using Kassyi.GSharp.Core;

namespace FileLocalRepro
{
    file struct LocalValue
    {
        public int A;
    }

    file struct LocalOuter
    {
        public struct Inner
        {
            public int B;
        }
    }

    public struct Pair
    {
        public int X;
        public int Y;
    }

    public struct Ctx
    {
        public int Calls;
    }

    public struct MacroValue
    {
        public int V;

        [GpuOperatorMacro(typeof(MacroFunctions), nameof(MacroFunctions.Add))]
        public static MacroValue operator +(MacroValue left, MacroValue right) => throw new GpuOnlyException();
    }

    file static partial class LocalMath
    {
        [GpuDeviceFunction]
        public static int Twice(int x) => x * 2;

        [GpuDeviceFunction]
        public static int Thrice(this int x) => x * 3;

        [GpuDeviceFunction]
        public static T Id<T>(T x) => x;
    }

    file static partial class PairExtensions
    {
        [GpuDeviceFunction]
        public static void Deconstruct(this Pair pair, out int x, out int y)
        {
            x = pair.X;
            y = pair.Y;
        }
    }

    // None of these bodies is copied: the managed path runs the user's method, so they may name file-local types
    public static partial class LocalKernels
    {
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

        [GpuKernel]
        public static void CallGeneric(int[] data)
        {
            data[ThreadIndex.X] = LocalMath.Id<int>(data[ThreadIndex.X]);
        }

        [GpuKernel]
        public static void Deconstruct(int[] data)
        {
            Pair pair = default;
            var (x, y) = pair;
            data[ThreadIndex.X] = x + y;
        }

        [GpuKernel]
        public static void Nested(int[] data)
        {
            LocalOuter.Inner inner = default;
            data[ThreadIndex.X] = inner.B;
        }
    }

    // The kernel waits at a barrier, so the managed path runs it as a state machine, which is a copy of its body
    public static partial class BarrierKernels
    {
        [GpuKernel]
        public static void Sync(int[] data)
        {
            LocalValue v = new LocalValue { A = 1 };
            Gpu.SyncThreads();
            data[ThreadIndex.X] = v.A;
        }
    }

    // The state machines of the device functions that suspend are copies, and so is the kernel that calls them
    public static partial class SuspendingKernels
    {
        [GpuDeviceFunction]
        public static int Wait(int value)
        {
            LocalValue v = new LocalValue { A = value };
            Gpu.SyncThreads();
            return v.A;
        }

        [GpuDeviceFunction]
        public static int WaitNested(int value)
        {
            LocalOuter.Inner inner = default;
            inner.B = value;
            Gpu.SyncThreads();
            return inner.B;
        }

        [GpuDeviceFunction]
        public static int WaitGeneric(int value)
        {
            int copy = LocalMath.Id<int>(value);
            Gpu.SyncThreads();
            return copy;
        }

        [GpuDeviceFunction]
        public static int WaitDeconstruct(Pair pair)
        {
            var (x, y) = pair;
            Gpu.SyncThreads();
            return x + y;
        }

        [GpuKernel]
        public static void Run(int[] data)
        {
            data[ThreadIndex.X] = Wait(data[ThreadIndex.X]);
        }
    }

    // An operator macro is expanded only by the managed path, which writes a managed version of the function
    public static partial class MacroFunctions
    {
        [GpuDeviceFunction]
        public static void Add(ref Ctx ctx, ref MacroValue result, MacroValue left, MacroValue right)
        {
            result.V = left.V + right.V;
        }

        [GpuDeviceFunction]
        public static int Rewritten(ref Ctx ctx, int a)
        {
            LocalValue v = new LocalValue { A = a };
            MacroValue x = new MacroValue { V = v.A };
            MacroValue z = default;
            z = x + x;
            return z.V;
        }

        [GpuKernel]
        public static void Run(int[] data)
        {
            Ctx ctx = default;
            data[ThreadIndex.X] = Rewritten(ref ctx, data[ThreadIndex.X]);
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
