namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Runtime;

public interface IGpuMemoryManager
{
    GpuBigIntMemoryHandle Allocate(int numWords);
    ScopedBigIntMemory AllocateTemp(int numWords);
    void Free(GpuBigIntMemoryHandle handle);
}
