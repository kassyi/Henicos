namespace Kassyi.GSharp.Stdlib.GpuMath.BigInt.Runtime;

public interface IGpuMemoryManager
{
    GpuBigIntMemoryHandle Allocate(int numWords);
    ScopedBigIntMemory AllocateTemp(int numWords);
    void Free(GpuBigIntMemoryHandle handle);
}
