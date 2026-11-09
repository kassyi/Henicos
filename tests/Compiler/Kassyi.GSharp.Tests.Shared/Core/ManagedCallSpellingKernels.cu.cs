using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

public struct SpellingInner
{
    public int V;
}

public struct SpellingCell
{
    public SpellingInner Inner;
}

// A generic struct whose constructor reaches a SharedMemory<T> of its own per closed type (#543)
public struct SpellingSlot<TTag> where TTag : unmanaged
{
    public int Value;

    public SpellingSlot(bool write, int lane, int inputValue)
    {
        var shared = new SharedMemory<int>(32);
        if (write)
        {
            shared[lane] = inputValue;
            Value = 0;
        }
        else
        {
            Value = shared[lane];
        }
    }
}

// Internal: the generator snapshot compiles this file against the test assembly, where a public extension method of the
// same name would make the calls ambiguous (CS0121)
internal static partial class SpellingInnerExtensions
{
    [GpuDeviceFunction]
    public static int Twice(this in SpellingInner inner) => inner.V * 2;
}

/// <summary>
/// Calls that the managed fallback spells out itself: an <c>in this</c> extension call in its static form, and the
/// type-key scope around a generic struct that declares <see cref="SharedMemory{T}"/>.
/// </summary>
public static partial class ManagedCallSpellingKernels
{
    [GpuDeviceFunction]
    public static SpellingCell MakeCell(int v) => new SpellingCell { Inner = new SpellingInner { V = v } };

    // The receiver of an in this extension call: a field of a call result, which is a value and takes no in; a field
    // of a local, a field of an array element and a field of a parameter, which are variables and keep it
    [GpuKernel]
    public static void InReceiverKernel(int[] values, SpellingCell[] cells, int[] result)
    {
        int tid = ThreadIndex.X;
        SpellingCell local = MakeCell(values[tid] * 10);
        result[tid] = MakeCell(values[tid]).Inner.Twice() + local.Inner.Twice() + cells[tid].Inner.Twice() +
                      FromParameter(MakeCell(values[tid] * 1000));
    }

    [GpuDeviceFunction]
    public static int FromParameter(SpellingCell cell) => cell.Inner.Twice();

    // A generic struct that declares SharedMemory<T>, created with a target-typed new(...): each thread writes its
    // value into the slot of the struct type and reads the one of the neighbouring thread
    [GpuKernel]
    public static void TargetTypedSlotKernel(int[] values, int[] result)
    {
        int lane = ThreadIndex.X;
        SpellingSlot<int> write = new(true, lane, values[lane]);
        Gpu.SyncThreads();
        SpellingSlot<int> read = new(false, lane ^ 1, 0);
        result[lane] = read.Value + write.Value;
    }
}
