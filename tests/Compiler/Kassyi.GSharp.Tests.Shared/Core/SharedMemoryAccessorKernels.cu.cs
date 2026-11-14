// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// A struct whose accessors create a <c>SharedMemory&lt;T&gt;</c> (#564): a getter, a setter and an indexer. Each thread
/// writes and reads its own element, so no barrier is needed.
/// </summary>
public struct SharedMemoryAccessors
{
    public int Base;
    public int Stored;

    public int Read
    {
        get
        {
            var memory = new SharedMemory<int>(32);
            memory[ThreadIndex.X] = Base + ThreadIndex.X;
            return memory[ThreadIndex.X];
        }
    }

    public int Write
    {
        set
        {
            var memory = new SharedMemory<int>(32);
            memory[ThreadIndex.X] = value + 1;
            Stored = memory[ThreadIndex.X];
        }
    }

    public int this[int offset]
    {
        get
        {
            var memory = new SharedMemory<int>(32);
            memory[ThreadIndex.X] = offset + ThreadIndex.X;
            return memory[ThreadIndex.X];
        }
    }
}

public static partial class SharedMemoryAccessorKernels
{
    [GpuKernel]
    public static void AccessorsKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var accessors = new SharedMemoryAccessors { Base = 100 };
        accessors.Write = 5;
        output[tid] = accessors.Read + accessors.Stored + accessors[1000];
    }
}
