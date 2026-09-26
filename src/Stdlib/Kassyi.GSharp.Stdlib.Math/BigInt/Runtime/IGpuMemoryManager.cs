namespace Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

public interface IGpuMemoryManager
{
    BigIntMemoryHandle Allocate(int numWords);
    ScopedBigIntMemory AllocateTemp(int numWords);
    void Free(BigIntMemoryHandle handle);
}
