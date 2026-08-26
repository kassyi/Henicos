using System;

namespace Kassyi.GSharp.Stdlib.Math.Providers;

public readonly ref struct ScopedGpuMemory
{
    private readonly IGpuExecutionProvider _provider;
    public readonly IntPtr Pointer;

    public ScopedGpuMemory(IGpuExecutionProvider provider, int numWords)
    {
        _provider = provider;
        Pointer = provider.AllocateMemory(numWords);
    }

    public void Dispose()
    {
        if (Pointer != IntPtr.Zero)
        {
            _provider.FreeMemory(Pointer);
        }
    }
}
