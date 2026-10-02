namespace Kassyi.GSharp.Stdlib.Math.GpuBigInt.Runtime;

public interface IGpuMemoryManager
{
    GpuBigIntMemoryHandle Allocate(int numWords);
    ScopedBigIntMemory AllocateTemp(int numWords);
    void Free(GpuBigIntMemoryHandle handle);
}
