namespace Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

using System;

public readonly ref struct BigIntExecutionContext(
    GpuContext gpuContext,
    IGpuMemoryManager memoryManager,
    IGpuBigIntArithmetic arithmetic,
    IntPtr stream)
{
    public GpuContext GpuContext { get; } = gpuContext;
    public IGpuMemoryManager MemoryManager { get; } = memoryManager;
    public IGpuBigIntArithmetic Arithmetic { get; } = arithmetic;
    public IntPtr Stream { get; } = stream;
}
