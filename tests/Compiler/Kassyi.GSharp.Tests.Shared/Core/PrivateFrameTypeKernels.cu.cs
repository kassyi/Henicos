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
