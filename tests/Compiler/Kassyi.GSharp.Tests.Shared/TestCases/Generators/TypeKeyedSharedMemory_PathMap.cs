namespace Repro;

using Kassyi.GSharp.Core;

public struct PathMapHolder<T> where T : unmanaged
{
    public PathMapHolder(int n)
    {
        var smem = new SharedMemory<int>(n);
    }
}
