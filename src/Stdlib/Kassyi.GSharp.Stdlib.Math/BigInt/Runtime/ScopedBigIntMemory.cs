namespace Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

public ref struct ScopedBigIntMemory(IGpuMemoryManager? manager, BigIntMemoryHandle handle)
{
    public BigIntMemoryHandle Handle { get; } = handle;

    public void Dispose()
    {
        if (manager != null && !Handle.IsDefault)
        {
            manager.Free(Handle);
        }
    }

    public static implicit operator BigIntMemoryHandle(in ScopedBigIntMemory memory) => memory.Handle;
}
