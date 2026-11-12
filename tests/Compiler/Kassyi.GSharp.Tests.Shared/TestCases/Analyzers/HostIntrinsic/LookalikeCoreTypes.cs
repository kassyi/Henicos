using System;

namespace Lookalike;

// User types that share the names of the GPU-only types of Kassyi.GSharp.Core; their members run on the host
public static class ThreadIndex
{
    public static int X => 0;
}

public struct Dim3
{
    public int X => 1;
}

public static class Gpu
{
    public static void SyncThreads()
    {
    }

    public static void ForUnroll(int start, int count, Action<int> body)
    {
    }
}

public readonly ref struct SharedMemory<T> where T : unmanaged
{
    public T this[int index] => default;
}
