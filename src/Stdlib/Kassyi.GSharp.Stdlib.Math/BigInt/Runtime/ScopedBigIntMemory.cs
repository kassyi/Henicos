namespace Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

public ref struct ScopedBigIntMemory(IGpuMemoryManager? manager, BigIntMemoryHandle handle)
{
    private IGpuMemoryManager? _manager = manager;
    private BigIntMemoryHandle _handle = handle;

    public BigIntMemoryHandle Handle => _handle;

    public void Dispose()
    {
        if (_manager != null && !_handle.IsDefault)
        {
            _manager.Free(_handle);
            _manager = null;
            _handle = default;
        }
    }

    public static implicit operator BigIntMemoryHandle(in ScopedBigIntMemory memory) => memory.Handle;
}
