namespace Kassyi.GSharp.Stdlib.Math.Providers;

public ref struct ScopedGpuMemory
{
    private readonly IGpuExecutionProvider _provider;
    public IntPtr Pointer { get; private set; }

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
            Pointer = IntPtr.Zero;
        }
    }
}
