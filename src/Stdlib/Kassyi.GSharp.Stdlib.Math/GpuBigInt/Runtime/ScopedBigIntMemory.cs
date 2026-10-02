namespace Kassyi.GSharp.Stdlib.Math.GpuBigInt.Runtime;

public ref struct ScopedBigIntMemory(IGpuMemoryManager? manager, GpuBigIntMemoryHandle handle)
{
    private IGpuMemoryManager? _manager = manager;
    private GpuBigIntMemoryHandle _handle = handle;

    public GpuBigIntMemoryHandle Handle => _handle;

    public void Dispose()
    {
        if (_manager != null && !_handle.IsDefault)
        {
            _manager.Free(_handle);
            _manager = null;
            _handle = default;
        }
    }

    public static implicit operator GpuBigIntMemoryHandle(in ScopedBigIntMemory memory) => memory.Handle;
}
