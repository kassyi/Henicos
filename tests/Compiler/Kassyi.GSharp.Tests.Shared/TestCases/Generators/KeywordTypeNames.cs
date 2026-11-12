using Kassyi.GSharp.Core;

namespace KeywordTypeNames;

// A generic struct whose name is a keyword, and whose constructor declares shared memory: the managed path wraps the
// creation in a type-key scope that names the struct type
public struct @class<T> where T : unmanaged
{
    public int Count;

    public @class(int count)
    {
        var shared = new SharedMemory<int>(4);
        shared[0] = count;
        Count = shared[0];
    }
}

public struct Slot<T> where T : unmanaged
{
    public int Count;

    public Slot(int count)
    {
        var shared = new SharedMemory<int>(4);
        shared[0] = count;
        Count = shared[0];
    }
}

// A type argument whose name is a keyword
public struct @struct
{
    public int Value;
}

public static partial class KeywordKernels
{
    [GpuKernel]
    public static void KeywordStructKernel(int[] output)
    {
        var keywordStruct = new @class<int>(output[0]);
        var keywordArgument = new Slot<@struct>(output[1]);
        output[0] = keywordStruct.Count + keywordArgument.Count;
    }
}
