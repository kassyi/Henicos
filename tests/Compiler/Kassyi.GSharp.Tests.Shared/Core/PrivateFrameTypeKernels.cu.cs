using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

// Device functions that wait at a barrier and keep a private nested struct across it. The managed fallback runs them as
// state machines whose frames hold that struct: the frames are generated into the function's class, where C# lets them
// name it (#1835).
public static partial class PrivateFrameTypeKernels
{
    private struct State
    {
        public int X;
    }

    // 2v + 1
    [GpuDeviceFunction]
    private static int Step(int[] data)
    {
        var state = new State { X = data[ThreadIndex.X] * 2 };
        Gpu.SyncThreads();
        return state.X + 1;
    }

    // Step(v) + 10 * v, with its own State across a second barrier: its frame holds Step's frame
    [GpuDeviceFunction]
    private static int StepTwice(int[] data)
    {
        State before;
        before.X = data[ThreadIndex.X];
        var stepped = Step(data);
        Gpu.SyncThreads();
        return stepped + before.X * 10;
    }

    // A private struct passed as a parameter, and one returned, by suspending functions: Scale(Build(v), 3) = 6v + 1
    [GpuDeviceFunction]
    private static State Build(int[] data)
    {
        Gpu.SyncThreads();
        return new State { X = data[ThreadIndex.X] * 2 };
    }

    [GpuDeviceFunction]
    private static int Scale(State state, int factor)
    {
        Gpu.SyncThreads();
        return state.X * factor + 1;
    }

    [GpuKernel]
    public static void StructParameterKernel(int[] data, int[] result) =>
        result[ThreadIndex.X] = Scale(Build(data), 3);

    // The struct of the class made and consumed by functions that do not suspend: SpillMake(v) is a temporary that the
    // frame of SpillFunction keeps across SpillWait, though no local, parameter or return value of the function has the
    // type of the struct
    [GpuDeviceFunction]
    private static State SpillMake(int[] data) => new State { X = data[ThreadIndex.X] * 2 };

    [GpuDeviceFunction]
    private static int SpillUse(State state, int waited) => state.X + waited;

    [GpuDeviceFunction]
    internal static int SpillWait(int[] data)
    {
        var value = data[ThreadIndex.X];
        Gpu.SyncThreads();
        return value;
    }

    // 2v + v
    [GpuDeviceFunction]
    internal static int SpillFunction(int[] data) => SpillUse(SpillMake(data), SpillWait(data));

    [GpuKernel]
    public static void SpillSameClassKernel(int[] data, int[] result) =>
        result[ThreadIndex.X] = SpillFunction(data);

    // The function, the struct and the kernel in one class
    [GpuKernel]
    public static void SameClassKernel(int[] data, int[] result) => result[ThreadIndex.X] = Step(data);

    // A function whose frame holds the private frame of another
    [GpuKernel]
    public static void SameClassChainKernel(int[] data, int[] result) => result[ThreadIndex.X] = StepTwice(data);

    // The struct of the outer class in a function of a nested class
    [GpuKernel]
    public static void NestedClassKernel(int[] data, int[] result) => result[ThreadIndex.X] = Nested.Step(data);

    // The struct of the outer class in a function of a nested generic class
    [GpuKernel]
    public static void GenericClassKernel(int[] data, int[] result) =>
        result[ThreadIndex.X] = Generic<int>.Step(data, 3);

    // 4v + 7, callable from other classes: its frame holds State, so it is private to this class and the callers
    // elsewhere reach it through the entries generated beside it
    [GpuDeviceFunction]
    internal static int Shared(int[] data)
    {
        var state = new State { X = data[ThreadIndex.X] * 4 };
        Gpu.SyncThreads();
        return state.X + 7;
    }

    // The value * factor of the neighbouring lane (lane ^ 1), read at a warp shuffle after the barrier
    [GpuDeviceFunction]
    internal static int Exchange(int[] data, int factor)
    {
        var state = new State { X = data[ThreadIndex.X] * factor };
        Gpu.SyncThreads();
        return Warp.ShuffleXorSync(0xFu, state.X, 1);
    }

    private static partial class Nested
    {
        // 3v - 1
        [GpuDeviceFunction]
        internal static int Step(int[] data)
        {
            var state = new State { X = data[ThreadIndex.X] * 3 };
            Gpu.SyncThreads();
            return state.X - 1;
        }
    }

    private static partial class Generic<T> where T : unmanaged
    {
        // v * factor + 100
        [GpuDeviceFunction]
        internal static int Step(int[] data, int factor)
        {
            var state = new State { X = data[ThreadIndex.X] };
            Gpu.SyncThreads();
            return state.X * factor + 100;
        }
    }
}

// Device functions and kernels of other classes that call PrivateFrameTypeKernels.Shared, whose frame they cannot name
public static partial class PrivateFrameTypeRelay
{
    // Shared(v) * 2 + v, with its own value across a second barrier: its frame holds Shared's frame as an object
    [GpuDeviceFunction]
    internal static int Relay(int[] data)
    {
        var before = data[ThreadIndex.X];
        var shared = PrivateFrameTypeKernels.Shared(data);
        Gpu.SyncThreads();
        return shared * 2 + before;
    }
}

public static partial class PrivateFrameTypeCallers
{
    // The private frame held by the kernel's context
    [GpuKernel]
    public static void OtherClassKernel(int[] data, int[] result) =>
        result[ThreadIndex.X] = PrivateFrameTypeKernels.Shared(data);

    // The private frame held by the frame of a device function of a third class
    [GpuKernel]
    public static void OtherClassChainKernel(int[] data, int[] result) =>
        result[ThreadIndex.X] = PrivateFrameTypeRelay.Relay(data);

    // Two private frames of the same function, each with its own warp path: a frame that is not numbered would meet the
    // other one's shuffle
    [GpuKernel]
    public static void OtherClassWarpKernel(int[] data, int[] result)
    {
        var first = PrivateFrameTypeKernels.Exchange(data, 1);
        var second = PrivateFrameTypeKernels.Exchange(data, 10);
        result[ThreadIndex.X] = first + second;
    }

    // The frame whose only private value is a spilled temporary, from a kernel of another class
    [GpuKernel]
    public static void SpillOtherClassKernel(int[] data, int[] result) =>
        result[ThreadIndex.X] = PrivateFrameTypeKernels.SpillFunction(data);

    // An unsafe function's private frame, a struct, held by the context of a kernel that is not unsafe
    [GpuKernel]
    public static void PointerFrameKernel(int[] data, int[] result) =>
        result[ThreadIndex.X] = PrivateFramePointerOwner.Step(data);
}

// An unsafe class: the frames of its device functions are structs taken by pointer
public static unsafe partial class PrivateFramePointerOwner
{
    private struct Pair
    {
        public int A;
        public int B;
    }

    // 5v + 3, through a pointer to a private struct; the pointer into the frame is kept across the barrier, so the
    // frame must not move between two calls of the state machine
    [GpuDeviceFunction]
    internal static int Step(int[] data)
    {
        var pair = new Pair { A = data[ThreadIndex.X] * 5, B = 2 };
        Pair* p = &pair;
        p->A += 1;
        Gpu.SyncThreads();
        return p->A + pair.B;
    }
}

// A base class that is not static, with a protected function whose frame holds a private type of the base: only the
// derived classes call it, and they reach the private frame through entries that are protected as well
public partial class PrivateFrameTypeBase
{
    private struct Scaled
    {
        public int X;
    }

    // 6v + 1
    [GpuDeviceFunction]
    protected static int Scale(int[] data)
    {
        var scaled = new Scaled { X = data[ThreadIndex.X] * 6 };
        Gpu.SyncThreads();
        return scaled.X + 1;
    }
}

public partial class PrivateFrameTypeDerived : PrivateFrameTypeBase
{
    [GpuKernel]
    public static void DerivedClassKernel(int[] data, int[] result) => result[ThreadIndex.X] = Scale(data);
}
